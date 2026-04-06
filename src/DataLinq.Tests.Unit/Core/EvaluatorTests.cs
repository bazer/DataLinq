using System.Linq.Expressions;
using System.Threading.Tasks;
using DataLinq.Linq;
using DataLinq.Tests.Models.Employees;

namespace DataLinq.Tests.Unit.Core;

public class EvaluatorTests
{
    [Test]
    public async Task PartialEval_EvaluatesLocalVariableIntoConstantExpression()
    {
        var localId = 12345;
        Expression<System.Func<Employee, bool>> expression = employee => employee.emp_no == localId;

        var binaryExpression = (BinaryExpression)expression.Body;
        var result = Evaluator.PartialEval(binaryExpression.Right);
        var constantExpression = result as ConstantExpression;

        await Assert.That(constantExpression).IsNotNull();
        await Assert.That(constantExpression!.Value).IsEqualTo(12345);
    }
}
