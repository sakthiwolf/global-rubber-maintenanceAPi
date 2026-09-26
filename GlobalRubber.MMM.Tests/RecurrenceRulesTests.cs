using System.Globalization;
using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Rules;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The recurring Machine PM cycle (RecurrenceRules), against the approved examples (2026-09-25): keep the ORIGINAL
/// cycle anchored to the checklist's creation date, skip missed dates, never create a backlog, never drift at month or
/// leap-year ends.
/// </summary>
public class RecurrenceRulesTests
{
    private static DateOnly D(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ================================================================ approved examples

    [Fact]
    public void Daily_CompletedLate_SkipsMissedDates_NextIsTheDayAfterCompletion()
    {
        // Due 25-Sep, completed 27-Sep -> 28-Sep (26 and 27 are never created).
        Assert.Equal(D("2026-09-28"), RecurrenceRules.NextDueAfterCompletion(D("2026-09-25"), ChecklistFrequency.Daily, D("2026-09-25"), D("2026-09-27")));
    }

    [Fact]
    public void Daily_ManyMissedCycles_OnlyTheFirstDateAfterCompletion()
    {
        // Due 20-Sep, completed 27-Sep -> 28-Sep; one date, no backlog.
        Assert.Equal(D("2026-09-28"), RecurrenceRules.NextDueAfterCompletion(D("2026-09-20"), ChecklistFrequency.Daily, D("2026-09-20"), D("2026-09-27")));
    }

    [Fact]
    public void Daily_CompletedOnTime_NextIsTomorrow()
    {
        Assert.Equal(D("2026-09-26"), RecurrenceRules.NextDueAfterCompletion(D("2026-09-25"), ChecklistFrequency.Daily, D("2026-09-25"), D("2026-09-25")));
    }

    [Fact]
    public void Weekly_CompletedLate_KeepsTheCycleWeekday_NotTheCompletionWeekday()
    {
        // Due 25-Sep, completed 27-Sep -> 02-Oct (same weekday as the cycle), NOT 04-Oct (completion + 7).
        var next = RecurrenceRules.NextDueAfterCompletion(D("2026-09-25"), ChecklistFrequency.Weekly, D("2026-09-25"), D("2026-09-27"));

        Assert.Equal(D("2026-10-02"), next);
        Assert.Equal(D("2026-09-25").DayOfWeek, next.DayOfWeek);
        Assert.NotEqual(D("2026-10-04"), next);
    }

    [Fact]
    public void Monthly_CompletedLate_KeepsTheCycleDay()
    {
        // Monthly on the 10th: due 10-Sep, completed 15-Sep -> 10-Oct (not 15-Oct).
        Assert.Equal(D("2026-10-10"), RecurrenceRules.NextDueAfterCompletion(D("2026-08-10"), ChecklistFrequency.Monthly, D("2026-09-10"), D("2026-09-15")));
    }

    [Fact]
    public void Yearly_CompletedAfterTheDate_NextIsTheSameDateNextYear()
    {
        Assert.Equal(D("2027-09-25"), RecurrenceRules.NextDueAfterCompletion(D("2026-09-25"), ChecklistFrequency.Yearly, D("2026-09-25"), D("2026-10-03")));
    }

    // ================================================================ month ends and leap years never drift

    [Fact]
    public void Monthly_31st_ClampsInShortMonths_ThenReturnsToThe31st()
    {
        var anchor = D("2027-01-31");

        var cycle = Enumerable.Range(0, 6).Select(n => RecurrenceRules.OccurrenceAt(anchor, ChecklistFrequency.Monthly, n)).ToList();

        Assert.Equal(new[] { D("2027-01-31"), D("2027-02-28"), D("2027-03-31"), D("2027-04-30"), D("2027-05-31"), D("2027-06-30") }, cycle);
    }

    [Fact]
    public void Monthly_31st_AfterCompletingTheFebruaryOccurrence_NextIsThe31stOfMarch_NotThe28th()
    {
        // The drift the anchor prevents: stepping from 28-Feb would give 28-Mar.
        Assert.Equal(D("2027-03-31"), RecurrenceRules.NextDueAfterCompletion(D("2027-01-31"), ChecklistFrequency.Monthly, D("2027-02-28"), D("2027-02-28")));
    }

    [Fact]
    public void Monthly_31st_InALeapYear_UsesThe29thOfFebruary()
    {
        Assert.Equal(D("2028-02-29"), RecurrenceRules.OccurrenceAt(D("2028-01-31"), ChecklistFrequency.Monthly, 1));
    }

    [Fact]
    public void Yearly_LeapDay_UsesThe28thInCommonYears_AndThe29thAgainInLeapYears()
    {
        var anchor = D("2028-02-29");

        var cycle = Enumerable.Range(0, 5).Select(n => RecurrenceRules.OccurrenceAt(anchor, ChecklistFrequency.Yearly, n)).ToList();

        Assert.Equal(new[] { D("2028-02-29"), D("2029-02-28"), D("2030-02-28"), D("2031-02-28"), D("2032-02-29") }, cycle);
        Assert.Equal(D("2032-02-29"), RecurrenceRules.NextDueAfterCompletion(anchor, ChecklistFrequency.Yearly, D("2031-02-28"), D("2031-03-01")));
    }

    // ================================================================ no backlog, no duplicate occurrence

    [Theory]
    [InlineData("Daily", "2026-01-01", "2026-03-10", "2026-06-20", "2026-06-21")]
    [InlineData("Weekly", "2026-01-01", "2026-03-12", "2026-06-20", "2026-06-25")]
    [InlineData("Monthly", "2026-01-10", "2026-03-10", "2026-06-20", "2026-07-10")]
    [InlineData("Yearly", "2020-06-01", "2022-06-01", "2026-06-20", "2027-06-01")]
    public void MonthsOfMissedCycles_SkipToTheFirstCycleDateAfterCompletion(string frequency, string anchor, string due, string completed, string expected)
    {
        var next = RecurrenceRules.NextDueAfterCompletion(D(anchor), frequency, D(due), D(completed));

        Assert.Equal(D(expected), next);
        Assert.True(next > D(completed)); // never already overdue
    }

    [Fact]
    public void CompletingEarly_NeverRecreatesTheSameOccurrence()
    {
        // Monthly due 25-Oct, completed early on 01-Oct: the successor is 25-Nov, not another 25-Oct.
        Assert.Equal(D("2026-11-25"), RecurrenceRules.NextDueAfterCompletion(D("2026-09-25"), ChecklistFrequency.Monthly, D("2026-10-25"), D("2026-10-01")));
    }

    [Theory]
    [InlineData("Daily")]
    [InlineData("Weekly")]
    [InlineData("Monthly")]
    [InlineData("Yearly")]
    public void TheSuccessorIsAlwaysACycleDate_StrictlyAfterCompletion(string frequency)
    {
        var anchor = D("2026-01-31");
        for (var completed = D("2026-01-31"); completed <= D("2028-03-31"); completed = completed.AddDays(3))
        {
            var next = RecurrenceRules.NextDueAfterCompletion(anchor, frequency, anchor, completed);

            Assert.True(next > completed);
            Assert.Contains(next, Enumerable.Range(0, 800).Select(n => RecurrenceRules.OccurrenceAt(anchor, frequency, n)));
            Assert.True(RecurrenceRules.FirstAfter(anchor, frequency, completed) == next); // the FIRST such date: nothing skipped too far
        }
    }

    // ================================================================ first occurrence / re-dating (C1)

    [Fact]
    public void FirstOnOrAfter_BeforeOrOnTheAnchor_IsTheAnchor()
    {
        Assert.Equal(D("2026-09-25"), RecurrenceRules.FirstOnOrAfter(D("2026-09-25"), ChecklistFrequency.Weekly, D("2026-09-25")));
        Assert.Equal(D("2026-09-25"), RecurrenceRules.FirstOnOrAfter(D("2026-09-25"), ChecklistFrequency.Monthly, D("2026-01-01")));
    }

    [Fact]
    public void FirstOnOrAfter_ACycleDateItself_IsReturned()
    {
        Assert.Equal(D("2026-10-09"), RecurrenceRules.FirstOnOrAfter(D("2026-09-25"), ChecklistFrequency.Weekly, D("2026-10-09")));
    }

    [Fact]
    public void FrequencyChange_ReDatesToTheFirstNewCycleDateOnOrAfterToday_SameAnchor()
    {
        // Anchor 25-Sep; changed from Daily to Weekly on 29-Sep -> 02-Oct (anchor + 1 week).
        Assert.Equal(D("2026-10-02"), RecurrenceRules.FirstOnOrAfter(D("2026-09-25"), ChecklistFrequency.Weekly, D("2026-09-29")));
        // ... and to Monthly -> 25-Oct.
        Assert.Equal(D("2026-10-25"), RecurrenceRules.FirstOnOrAfter(D("2026-09-25"), ChecklistFrequency.Monthly, D("2026-09-29")));
    }

    [Fact]
    public void FarFutureDates_AreComputedDirectly()
    {
        Assert.Equal(D("2126-09-25"), RecurrenceRules.FirstOnOrAfter(D("2026-09-25"), ChecklistFrequency.Daily, D("2126-09-25")));
        Assert.Equal(D("2126-01-31"), RecurrenceRules.FirstOnOrAfter(D("2026-01-31"), ChecklistFrequency.Monthly, D("2126-01-15")));
    }

    // ================================================================ guards

    [Fact]
    public void AnUnknownFrequency_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => RecurrenceRules.OccurrenceAt(D("2026-09-25"), "Hourly", 1));
        Assert.Throws<ArgumentException>(() => RecurrenceRules.FirstOnOrAfter(D("2026-09-25"), "Hourly", D("2026-01-01")));
        Assert.Throws<ArgumentException>(() => RecurrenceRules.NextDueAfterCompletion(D("2026-09-25"), "Hourly", D("2026-09-25"), D("2026-09-26")));
    }

    [Fact]
    public void Frequencies_MatchTheDatabaseCheckConstraint()
    {
        Assert.Equal(new[] { "Daily", "Weekly", "Monthly", "Yearly" }, ChecklistFrequency.All); // CK_maintenance_checklist_master_frequency
    }

    // ================================================================ cycle anchor = creation date in plant time

    [Theory]
    [InlineData("2026-09-24T18:29:59", "2026-09-24")] // 23:59:59 IST
    [InlineData("2026-09-24T18:30:00", "2026-09-25")] // 00:00 IST - the UTC date is still the 24th
    [InlineData("2026-09-25T05:27:05", "2026-09-25")] // CHK-0001's created_at (10:57 IST)
    public void TheAnchor_IsTheCreationInstantAsAnIndiaStandardTimeDate(string utc, string expected)
    {
        var instant = DateTime.SpecifyKind(DateTime.Parse(utc, CultureInfo.InvariantCulture), DateTimeKind.Utc);

        Assert.Equal(D(expected), PlantTime.ToPlantDate(instant));
    }
}
