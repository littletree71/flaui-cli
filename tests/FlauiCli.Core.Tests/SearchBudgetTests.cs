using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Tests;

public class SearchBudgetTests
{
    [Fact]
    public void RemainingFollowsTheCommandTimeout()
    {
        Assert.Equal(SearchBudget.Default, SearchBudget.Remaining);

        using (SearchBudget.Begin(30_000))
        {
            Assert.InRange(SearchBudget.Remaining, TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(30));

            // Nested scopes restore the outer deadline
            using (SearchBudget.Begin(0)) Assert.Equal(SearchBudget.Minimum, SearchBudget.Remaining);
            Assert.True(SearchBudget.Remaining > TimeSpan.FromSeconds(29));
        }

        Assert.Equal(SearchBudget.Default, SearchBudget.Remaining);
    }
}
