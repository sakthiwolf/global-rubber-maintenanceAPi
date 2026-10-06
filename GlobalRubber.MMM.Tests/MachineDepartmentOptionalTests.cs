using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Machine Master Department is OPTIONAL (migration 023): null is valid on create and update, a supplied department must
/// still exist (404) and - when newly chosen - be active (400), the list can filter "no department", and nothing else about
/// a machine changes. Departments (DepartmentTestData): 1, 2 active; 3 inactive. Machines: 1 (dept 1), 2 (dept 3), 3 (dept 2).
/// </summary>
public class MachineDepartmentOptionalTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public MachineDepartmentOptionalTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Sut(MachineService Service, InMemoryMachineRepository Machines, RecordingAuditLog Audit);

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var audit = new RecordingAuditLog();
        return new Sut(new MachineService(machines, departments, users, new FixedClock(), audit, NullLogger<MachineService>.Instance), machines, audit);
    }

    private static CreateMachineRequest New(int? departmentId, string name = "Hydraulic Moulding") => new()
    {
        MachineName = name, MachineType = "Hydraulic Moulding", DepartmentId = departmentId, Location = "Shop Floor 1",
        MaintenanceFrequencyDays = 30, Criticality = "High",
    };

    private static UpdateMachineRequest Edit(Sut s, int id, int? departmentId)
    {
        var m = s.Machines.Stored(id);
        return new UpdateMachineRequest
        {
            MachineName = m.MachineName, MachineType = m.MachineType, DepartmentId = departmentId, Location = m.Location,
            Manufacturer = m.Manufacturer, Model = m.Model, SerialNumber = m.SerialNumber, Capacity = m.Capacity, InstallationDate = m.InstallationDate,
            MachineRange = m.MachineRange, Ownership = m.Ownership, PurchaseDate = m.PurchaseDate,
            MaintenanceFrequencyDays = m.MaintenanceFrequencyDays, Criticality = m.Criticality, Remarks = m.Remarks,
            RowVersion = Convert.ToBase64String(m.RowVersion),
        };
    }

    [Fact] // 1
    public async Task Create_WithADepartment_Succeeds()
    {
        var dto = await Create().Service.CreateAsync(New(1), 1, null, CancellationToken.None);
        Assert.Equal(((int?)1, "Injection Moulding", (bool?)true), (dto.DepartmentId, dto.DepartmentName, dto.DepartmentIsActive));
    }

    [Fact] // 2 + F01 compatibility
    public async Task Create_WithoutADepartment_Succeeds_AndStoresNull()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(null), 1, null, CancellationToken.None);

        Assert.Equal(((int?)null, (string?)null, (bool?)null), (dto.DepartmentId, dto.DepartmentName, dto.DepartmentIsActive));
        Assert.Null(s.Machines.Stored(dto.MachineId).DepartmentId);
        Assert.EndsWith("without a department.", s.Audit.Entries.Single().Description);
    }

    [Fact] // 3
    public async Task Update_FromADepartmentToNone_Succeeds_AndIsAudited()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(1, Edit(s, 1, null), 1, null, CancellationToken.None);

        Assert.Null(dto.DepartmentId);
        Assert.Null(s.Machines.Stored(1).DepartmentId);
        Assert.Contains(s.Audit.Entries.Last().Details, d => d is { FieldName: "department_code", OldValue: "DEP-0001", NewValue: null });
    }

    [Fact] // 4
    public async Task Update_FromNoneToAValidDepartment_Succeeds()
    {
        var s = Create();
        await s.Service.UpdateAsync(1, Edit(s, 1, null), 1, null, CancellationToken.None);

        var dto = await s.Service.UpdateAsync(1, Edit(s, 1, 2), 1, null, CancellationToken.None);

        Assert.Equal(((int?)2, (bool?)true), (dto.DepartmentId, dto.DepartmentIsActive));
        Assert.Contains(s.Audit.Entries.Last().Details, d => d is { FieldName: "department_code", OldValue: null });
    }

    [Fact] // 5
    public async Task ASuppliedDepartment_IsStillValidated()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(New(999), 1, null, CancellationToken.None));
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(New(3), 1, null, CancellationToken.None));
        Assert.Contains("The selected department is not active.", inactive.Errors);
        var zero = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(New(0), 1, null, CancellationToken.None));
        Assert.Contains("DepartmentId is not valid.", zero.Errors);
        await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s, 1, 3), 1, null, CancellationToken.None)); // newly chosen inactive
        Assert.Equal(1, s.Machines.Stored(1).DepartmentId);
    }

    [Fact]
    public async Task KeepingAnInactiveDepartment_IsStillAllowed()
    {
        var s = Create(); // machine 2 sits in the inactive department 3
        var dto = await s.Service.UpdateAsync(2, Edit(s, 2, 3), 1, null, CancellationToken.None);
        Assert.Equal(((int?)3, (bool?)false), (dto.DepartmentId, dto.DepartmentIsActive));
    }

    [Fact] // 6 + 7 + filter
    public async Task GetAndList_WithNoDepartment_Succeed_AndTheListFiltersNoDepartment()
    {
        var s = Create();
        var none = await s.Service.CreateAsync(New(null, name: "Strip cutting machine"), 1, null, CancellationToken.None);

        var got = await s.Service.GetByIdAsync(none.MachineId, CancellationToken.None);
        Assert.Null(got.DepartmentName);

        async Task<List<string>> Codes(MachineListQuery q) => (await s.Service.GetAllAsync(q, CancellationToken.None)).Items.Select(i => i.MachineCode).ToList();
        Assert.Equal(4, (await Codes(new MachineListQuery { PageNumber = 1, PageSize = 50 })).Count);
        Assert.Equal(new[] { none.MachineCode }, await Codes(new MachineListQuery { PageNumber = 1, PageSize = 50, NoDepartment = true }));
        Assert.Equal(new[] { "MAC-0001" }, await Codes(new MachineListQuery { PageNumber = 1, PageSize = 50, DepartmentId = 1 }));
    }

    [Fact] // 8
    public async Task ExistingMachines_KeepTheirDepartments()
    {
        var s = Create();
        await s.Service.CreateAsync(New(null), 1, null, CancellationToken.None);
        Assert.Equal(new int?[] { 1, 3, 2 }, new[] { 1, 2, 3 }.Select(id => s.Machines.Stored(id).DepartmentId));
    }

    // ================================================================ HTTP: null in, null out

    [Fact]
    public async Task Http_PostAndPutAcceptNullDepartment_AndGetReturnsNull()
    {
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMachineRepository>();
            services.AddSingleton<IMachineRepository>(machines);
            services.RemoveAll<IDepartmentRepository>();
            services.AddSingleton<IDepartmentRepository>(departments);
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(new InMemoryUserRepository(UserTestData.Users(), roles.Find));
            services.RemoveAll<IAuditLogService>();
            services.AddSingleton<IAuditLogService>(new RecordingAuditLog());
            services.RemoveAll<IPermissionAuthorizationService>();
            services.AddSingleton<IPermissionAuthorizationService>(new StubPermissionAuthorization(true));
        }));
        var client = factory.CreateClient();
        TestAuth.Authenticate(client, factory, "ADMIN", 1);
        static StringContent Json(object o) => new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
        static async Task<JsonElement> Data(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("data").Clone();

        var created = await client.PostAsync("/api/v1/machines", Json(new
        {
            machineName = "Hydraulic Moulding", machineType = "Hydraulic Moulding", departmentId = (int?)null, location = "Shop Floor 1", maintenanceFrequencyDays = 30, criticality = "High",
        }));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = await Data(created);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("departmentId").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("departmentName").ValueKind);

        var omitted = await client.PostAsync("/api/v1/machines", Json(new
        {
            machineName = "Strip cutting machine", machineType = "Cutting", location = "Shop Floor 1", maintenanceFrequencyDays = 30, criticality = "Low", // no departmentId at all
        }));
        Assert.Equal(HttpStatusCode.Created, omitted.StatusCode);

        var id = data.GetProperty("machineId").GetInt32();
        var put = await client.PutAsync($"/api/v1/machines/{id}", Json(new
        {
            machineName = "Hydraulic Moulding", machineType = "Hydraulic Moulding", departmentId = 2, location = "Shop Floor 1", maintenanceFrequencyDays = 30,
            criticality = "High", rowVersion = data.GetProperty("rowVersion").GetString(),
        }));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(2, (await Data(put)).GetProperty("departmentId").GetInt32());

        var noDept = JsonDocument.Parse(await (await client.GetAsync("/api/v1/machines?noDepartment=true&pageSize=50")).Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("items");
        Assert.Equal(new[] { "Strip cutting machine" }, noDept.EnumerateArray().Select(i => i.GetProperty("machineName").GetString()));
    }
}
