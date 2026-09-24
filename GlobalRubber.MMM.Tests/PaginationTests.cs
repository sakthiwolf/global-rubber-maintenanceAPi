
using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Tests;

public class PaginationRequestTests
{
    [Fact]
    public void Defaults_MatchPaginationDefaults()
    {
        var request = new PaginationRequest();

        Assert.Equal(PaginationDefaults.DefaultPageNumber, request.PageNumber);
        Assert.Equal(PaginationDefaults.DefaultPageSize, request.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(3, 3)]
    public void PageNumber_ClampsBelowOneToOne(int input, int expected)
    {
        var request = new PaginationRequest { PageNumber = input };

        Assert.Equal(expected, request.PageNumber);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(50, 50)]
    [InlineData(1000, 100)]
    public void PageSize_ClampsToValidRange(int input, int expected)
    {
        var request = new PaginationRequest { PageSize = input };

        Assert.Equal(expected, request.PageSize);
    }
}

public class PagedResultTests
{
    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(100, 10, 10)]
    public void TotalPages_IsCalculatedFromTotalCountAndPageSize(int totalCount, int pageSize, int expectedTotalPages)
    {
        var result = PagedResult<string>.Create(Array.Empty<string>(), pageNumber: 1, pageSize, totalCount);

        Assert.Equal(expectedTotalPages, result.TotalPages);
    }

    [Fact]
    public void HasPreviousPage_IsFalseOnFirstPage()
    {
        var result = PagedResult<string>.Create(Array.Empty<string>(), pageNumber: 1, pageSize: 10, totalCount: 30);

        Assert.False(result.HasPreviousPage);
    }

    [Fact]
    public void HasPreviousPage_IsTrueAfterFirstPage()
    {
        var result = PagedResult<string>.Create(Array.Empty<string>(), pageNumber: 2, pageSize: 10, totalCount: 30);

        Assert.True(result.HasPreviousPage);
    }

    [Fact]
    public void HasNextPage_IsTrueBeforeLastPage()
    {
        var result = PagedResult<string>.Create(Array.Empty<string>(), pageNumber: 1, pageSize: 10, totalCount: 30);

        Assert.True(result.HasNextPage);
    }

    [Fact]
    public void HasNextPage_IsFalseOnLastPage()
    {
        var result = PagedResult<string>.Create(Array.Empty<string>(), pageNumber: 3, pageSize: 10, totalCount: 30);

        Assert.False(result.HasNextPage);
    }

    [Fact]
    public void Empty_ReturnsZeroItemsAndZeroTotalCount()
    {
        var result = PagedResult<string>.Empty(pageNumber: 1, pageSize: 10);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }
}
