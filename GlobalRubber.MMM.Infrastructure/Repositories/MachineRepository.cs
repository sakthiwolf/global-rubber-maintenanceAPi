using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class MachineRepository : IMachineRepository
{
    private const string ConcurrencyMessage = "The machine was modified by another user. Refresh the machine and try again.";
    private const string SerialNumberIndex = "UX_machine_master_serial_number";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly IDocumentSequenceGenerator _documentSequence;

    public MachineRepository(GlobalRubberDbContext dbContext, IDocumentSequenceGenerator documentSequence)
    {
        _dbContext = dbContext;
        _documentSequence = documentSequence;
    }

    public async Task<(IReadOnlyList<Machine> Items, int TotalCount)> GetAllAsync(
        MachineListQuery request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Machines.AsNoTracking();

        if (request.IsActive is { } isActive)
        {
            query = query.Where(m => m.IsActive == isActive);
        }

        if (request.DepartmentId is { } departmentId)
        {
            query = query.Where(m => m.DepartmentId == departmentId);
        }

        var status = request.OperationalStatus?.Trim();
        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(m => m.OperationalStatus == status);
        }

        var search = request.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            // Explicit ToLower rather than relying on the column collation being case-insensitive.
            var lowered = search.ToLower();
            query = query.Where(m => m.MachineCode.ToLower().Contains(lowered) || m.MachineName.ToLower().Contains(lowered));
        }

        var ordered = query.OrderBy(m => m.MachineName).ThenBy(m => m.MachineId);

        var totalCount = await ordered.CountAsync(cancellationToken);

        var items = await ordered
            .Include(m => m.Department)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Machine?> GetByIdAsync(int machineId, CancellationToken cancellationToken) =>
        _dbContext.Machines
            .AsNoTracking()
            .Include(m => m.Department)
            .FirstOrDefaultAsync(m => m.MachineId == machineId, cancellationToken);

    public Task<bool> ExistsActiveByNameAsync(string machineName, int? excludeMachineId, CancellationToken cancellationToken)
    {
        var lowered = machineName.ToLower();

        return _dbContext.Machines.AsNoTracking()
            .AnyAsync(m => m.IsActive && m.MachineName.ToLower() == lowered && m.MachineId != excludeMachineId, cancellationToken);
    }

    public Task<bool> ExistsBySerialNumberAsync(string serialNumber, int? excludeMachineId, CancellationToken cancellationToken)
    {
        var lowered = serialNumber.ToLower();

        return _dbContext.Machines.AsNoTracking()
            .AnyAsync(m => m.SerialNumber != null && m.SerialNumber.ToLower() == lowered && m.MachineId != excludeMachineId, cancellationToken);
    }

    public async Task<Machine> AddAsync(Machine machine, CancellationToken cancellationToken)
    {
        // The DbContext is configured with EnableRetryOnFailure, so a transaction we start ourselves must run inside
        // the execution strategy. If the caller already has a transaction open (a verification harness), join it.
        var strategy = _dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                _dbContext.Entry(machine).State = EntityState.Detached; // a retry must not see the previous attempt's tracking

                var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
                await using var transaction = ownsTransaction
                    ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
                    : null;

                machine.MachineCode = await _documentSequence.NextCodeAsync(DocumentTypes.Machine, cancellationToken);
                _dbContext.Machines.Add(machine);
                await _dbContext.SaveChangesAsync(cancellationToken);

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            });
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out var serialNumberTaken))
        {
            _dbContext.Entry(machine).State = EntityState.Detached;

            // The service checks both first; this is the race where another save got in between. The index name is only
            // inspected here, never sent to the client.
            throw new ConflictException(serialNumberTaken
                ? $"A machine with serial number '{machine.SerialNumber}' already exists."
                : "The machine code could not be issued because it already exists. Please try again.");
        }

        // Keep the generated id/code/row_version but stop tracking: a later write in the same scope attaches its own stub.
        _dbContext.Entry(machine).State = EntityState.Detached;

        return machine;
    }

    public async Task<Machine> UpdateAsync(Machine machine, byte[] originalRowVersion, CancellationToken cancellationToken)
    {
        // Attach a stub (key + the caller's row version + only the values this operation writes): no navigations, and
        // the row version the CALLER holds becomes the ORIGINAL value in the UPDATE's WHERE clause.
        var stub = new Machine
        {
            MachineId = machine.MachineId,
            RowVersion = originalRowVersion,
            MachineName = machine.MachineName,
            MachineType = machine.MachineType,
            DepartmentId = machine.DepartmentId,
            Location = machine.Location,
            Manufacturer = machine.Manufacturer,
            Model = machine.Model,
            SerialNumber = machine.SerialNumber,
            Capacity = machine.Capacity,
            InstallationDate = machine.InstallationDate,
            MaintenanceFrequencyDays = machine.MaintenanceFrequencyDays,
            Criticality = machine.Criticality,
            Remarks = machine.Remarks,
            UpdatedAt = machine.UpdatedAt,
            UpdatedBy = machine.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(m => m.MachineName).IsModified = true;
        entry.Property(m => m.MachineType).IsModified = true;
        entry.Property(m => m.DepartmentId).IsModified = true;
        entry.Property(m => m.Location).IsModified = true;
        entry.Property(m => m.Manufacturer).IsModified = true;
        entry.Property(m => m.Model).IsModified = true;
        entry.Property(m => m.SerialNumber).IsModified = true;
        entry.Property(m => m.Capacity).IsModified = true;
        entry.Property(m => m.InstallationDate).IsModified = true;
        entry.Property(m => m.MaintenanceFrequencyDays).IsModified = true;
        // Responsible Engineer temporarily disabled. Database field and relationship intentionally retained for future re-enablement.
        // responsible_engineer_id is deliberately NOT marked modified, so an update never overwrites or clears it.
        entry.Property(m => m.Criticality).IsModified = true;
        entry.Property(m => m.Remarks).IsModified = true;
        entry.Property(m => m.UpdatedAt).IsModified = true;
        entry.Property(m => m.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, out _))
        {
            entry.State = EntityState.Detached;

            // machine_code is never written here, so the only unique index an update can hit is the serial number's.
            throw new ConflictException($"A machine with serial number '{machine.SerialNumber}' already exists.");
        }

        machine.RowVersion = stub.RowVersion; // refreshed by SQL Server on save
        entry.State = EntityState.Detached;
        return machine;
    }

    public async Task<Machine> DeactivateAsync(Machine machine, CancellationToken cancellationToken)
    {
        // Stub carrying the row version the machine was loaded with; the ONLY columns marked modified are is_active,
        // updated_at and updated_by - the UPDATE cannot touch anything else, and no DELETE is ever issued.
        var stub = new Machine
        {
            MachineId = machine.MachineId,
            RowVersion = machine.RowVersion,
            IsActive = machine.IsActive,
            UpdatedAt = machine.UpdatedAt,
            UpdatedBy = machine.UpdatedBy,
        };

        var entry = _dbContext.Attach(stub);
        entry.Property(m => m.IsActive).IsModified = true;
        entry.Property(m => m.UpdatedAt).IsModified = true;
        entry.Property(m => m.UpdatedBy).IsModified = true;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;
            throw new ConflictException(ConcurrencyMessage);
        }

        machine.RowVersion = stub.RowVersion;
        entry.State = EntityState.Detached;
        return machine;
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex, out bool serialNumberTaken)
    {
        serialNumberTaken = false;
        if (ex.InnerException is not SqlException { Number: 2601 or 2627 } sql)
        {
            return false;
        }

        serialNumberTaken = sql.Message.Contains(SerialNumberIndex, StringComparison.OrdinalIgnoreCase);
        return true;
    }
}
