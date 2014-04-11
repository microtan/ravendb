
using System;
using ICSharpCode.NRefactory.CSharp;

namespace Raven.Database.Linq.Ast
{
	[CLSCompliant(false)]
	public class TransformNullCoalescingOperatorTransformer : DepthFirstAstVisitor<object,object>
	{
		/// <summary>
		/// We have to replace code such as:
		///		doc.FirstName ?? ""
		/// Into 
		///		doc.FirstName != null ? doc.FirstName : ""
		/// Because we use DynamicNullObject instead of null, and that preserve the null coallasing semantics.
		/// </summary>
		public override object VisitBinaryOperatorExpression(BinaryOperatorExpression binaryOperatorExpression, object data)
		{
			if(binaryOperatorExpression.Operator==BinaryOperatorType.NullCoalescing)
			{
				var node = new ConditionalExpression(
					new BinaryOperatorExpression(new MemberReferenceExpression(new ThisReferenceExpression(), "__dynamic_null")
						, BinaryOperatorType.InEquality,
						binaryOperatorExpression.Left.Clone()),
					binaryOperatorExpression.Left.Clone(),
					binaryOperatorExpression.Right.Clone()
					);
				binaryOperatorExpression.ReplaceWith(node);
				return null;
			}

			return base.VisitBinaryOperatorExpression(binaryOperatorExpression, data);
		}
	}
}
