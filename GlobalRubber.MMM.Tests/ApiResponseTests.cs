using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Tests;

public class ApiResponseTests
{
    [Fact]
    public void Ok_NonGeneric_SetsSuccessTrueAndDefaultMessage()
    {
        var response = ApiResponse.Ok();

        Assert.True(response.Success);
        Assert.Equal("Request completed successfully.", response.Message);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void Fail_NonGeneric_SetsSuccessFalseAndErrors()
    {
        var response = ApiResponse.Fail("Something went wrong.", new[] { "Detail 1", "Detail 2" });

        Assert.False(response.Success);
        Assert.Equal("Something went wrong.", response.Message);
        Assert.Equal(new[] { "Detail 1", "Detail 2" }, response.Errors);
    }

    [Fact]
    public void Fail_NonGeneric_WithoutErrors_ReturnsEmptyErrorsList()
    {
        var response = ApiResponse.Fail("Something went wrong.");

        Assert.False(response.Success);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public void Ok_Generic_CarriesDataAndSuccessTrue()
    {
        var response = ApiResponse<string>.Ok("payload");

        Assert.True(response.Success);
        Assert.Equal("payload", response.Data);
        Assert.Equal("Request completed successfully.", response.Message);
    }

    [Fact]
    public void Fail_Generic_LeavesDataNull()
    {
        var response = ApiResponse<string>.Fail("Not found.");

        Assert.False(response.Success);
        Assert.Null(response.Data);
    }

    [Fact]
    public void Fail_Generic_SingleErrorOverload_WrapsIntoList()
    {
        var response = ApiResponse<int>.Fail("Bad request.", "Field X is required.");

        Assert.Single(response.Errors);
        Assert.Equal("Field X is required.", response.Errors[0]);
    }
}
