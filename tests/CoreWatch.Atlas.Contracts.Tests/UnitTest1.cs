using CoreWatch.Atlas.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreWatch.Atlas.Contracts.Tests;

[TestClass]
public sealed class ContractsAssemblyTests
{
    [TestMethod]
    public void ContractsAssemblyHasExpectedName()
    {
        var assemblyName = typeof(AssemblyMarker).Assembly.GetName();

        Assert.AreEqual("CoreWatch.Atlas.Contracts", assemblyName.Name);
    }
}
