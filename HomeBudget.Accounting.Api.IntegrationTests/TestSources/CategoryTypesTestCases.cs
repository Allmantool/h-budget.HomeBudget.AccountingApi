using System.Collections;

using HomeBudget.Accounting.Domain.Enumerations;

namespace HomeBudget.Accounting.Api.IntegrationTests.TestSources
{
    internal class CategoryTypesTestCases : IEnumerable
    {
        public IEnumerator GetEnumerator()
        {
            yield return new object[] { CategoryTypes.Expense };
            yield return new object[] { CategoryTypes.Income };
        }
    }
}
