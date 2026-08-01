using System.Text;

namespace CoreWatch.Atlas.Server.Tests;

[TestClass]
public sealed class ReportExportTests
{
    [TestMethod]
    public void PdfExportHasValidHeaderAndTrailer()
    {
        var now=DateTimeOffset.UtcNow;var report=new ServerReport(Guid.NewGuid(),"host",now.AddDays(-1),now,10,99,new(20,30,25),new(40,50,45),new(60,65,63),[]);
        var text=Encoding.ASCII.GetString(ReportExports.Pdf(report));
        StringAssert.StartsWith(text,"%PDF-1.4");
        StringAssert.EndsWith(text,"%%EOF");
    }
}
