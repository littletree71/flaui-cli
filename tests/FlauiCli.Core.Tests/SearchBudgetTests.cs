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

    [Fact]
    public void ANewCommandForgetsAnIncompleteSearch()
    {
        SearchBudget.ReportIncomplete("stopped");
        Assert.Equal("stopped", SearchBudget.Incomplete);
        using (SearchBudget.Begin(1_000)) Assert.Null(SearchBudget.Incomplete);

        SearchBudget.ReportIncomplete("stopped");
        SearchBudget.ClearIncomplete();
        Assert.Null(SearchBudget.Incomplete);
    }

    [Fact]
    public void CancellationEndsTheBudget()
    {
        using var cancel = new CancellationTokenSource();
        using (SearchBudget.Begin(30_000, cancel.Token))
        {
            SearchBudget.ThrowIfCancelled();
            Assert.False(SearchBudget.IsCancelled);
            cancel.Cancel();
            Assert.True(SearchBudget.IsCancelled);
            Assert.Equal(TimeSpan.Zero, SearchBudget.Remaining);
            Assert.Throws<OperationCanceledException>(SearchBudget.ThrowIfCancelled);

            // A nested scope without a token keeps the outer one
            using (SearchBudget.Begin(1_000)) Assert.True(SearchBudget.IsCancelled);
        }
        Assert.False(SearchBudget.IsCancelled);
    }
}
