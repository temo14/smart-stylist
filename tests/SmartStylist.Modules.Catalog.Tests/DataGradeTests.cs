using SmartStylist.Modules.Catalog.Domain;
using SmartStylist.Modules.Catalog.Reporting;

namespace SmartStylist.Modules.Catalog.Tests;

public class DataGradeTests
{
    private static CollectionRun Run(RunStatus status, int products) => new() { Status = status, ProductsSeen = products };

    [Fact]
    public void No_run_yet() => Assert.Equal(DataGrade.NotRunYet, DataQualityReport.Grade(null, 0, 0));

    [Theory]
    [InlineData(RunStatus.Failed, 10, 1.0, 1.0, DataGrade.Failing)]
    [InlineData(RunStatus.Succeeded, 0, 1.0, 1.0, DataGrade.Failing)]
    [InlineData(RunStatus.Succeeded, 10, 0.95, 0.8, DataGrade.Clean)]
    [InlineData(RunStatus.Succeeded, 10, 0.95, 0.2, DataGrade.Usable)]
    [InlineData(RunStatus.Succeeded, 10, 0.7, 1.0, DataGrade.Usable)]
    [InlineData(RunStatus.Succeeded, 10, 0.4, 1.0, DataGrade.Poor)]
    public void Grades_latest_run(RunStatus status, int products, double pages, double sizes, DataGrade expected) =>
        Assert.Equal(expected, DataQualityReport.Grade(Run(status, products), pages, sizes));
}
