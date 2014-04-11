﻿using System;
using System.Collections.Generic;
using System.Linq;
using ICSharpCode.NRefactory.CSharp;
using ICSharpCode.NRefactory.PatternMatching;

namespace Raven.Database.Linq.Ast
{
	[CLSCompliant(false)]
	public class TransformDynamicLambdaExpressions : DepthFirstAstVisitor<object,object>
	{
		public override object VisitLambdaExpression(LambdaExpression lambdaExpression, object data)
		{
			var invocationExpression = lambdaExpression.Parent as InvocationExpression;
			if (invocationExpression == null)
				return base.VisitLambdaExpression(lambdaExpression, data);

			var target = invocationExpression.Target as MemberReferenceExpression;
			if(target == null)
				return base.VisitLambdaExpression(lambdaExpression, data);

			AstNode node = lambdaExpression;
			var parenthesizedlambdaExpression = new ParenthesizedExpression(lambdaExpression.Clone());
			switch (target.MemberName)
			{
				case "Sum":
				case "Average":
					node = ModifyLambdaForNumerics(lambdaExpression, parenthesizedlambdaExpression);
					break;
				case "Max":
				case "Min":
					node = ModifyLambdaForMinMax(lambdaExpression, parenthesizedlambdaExpression);
					break;
				case "OrderBy":
				case "OrderByDescending":
				case "GroupBy":
				case "Recurse":
				case "Select":
				case "ToDictionary":
					node = ModifyLambdaForSelect(parenthesizedlambdaExpression, target);
					break;
				case "SelectMany":
					node = ModifyLambdaForSelectMany(lambdaExpression, parenthesizedlambdaExpression, invocationExpression);
					break;
				case "Any":
				case "all":
				case "First":
				case "FirstOrDefault":
				case "Last":
				case "LastOfDefault":
				case "Single":
				case "Where":
				case "Count":
				case "SingleOrDefault":
					node = new CastExpression(new SimpleType("Func<dynamic, bool>"), parenthesizedlambdaExpression.Clone());
				break;
			}
			lambdaExpression.ReplaceWith(node);

			if (node != lambdaExpression)
				return node.AcceptVisitor(this, null);
			return base.VisitLambdaExpression(lambdaExpression, null);
		}

		private static AstNode ModifyLambdaForSelect(ParenthesizedExpression parenthesizedlambdaExpression,
		                                           MemberReferenceExpression target)
		{
			var parentInvocation = target.Target as InvocationExpression;
			if(parentInvocation != null)
			{
				var parentTarget = parentInvocation.Target as MemberReferenceExpression;
				if(parentTarget != null)
				{
                    if(parentTarget.MemberName == "GroupBy")
					    return new CastExpression(new SimpleType("Func<IGrouping<dynamic,dynamic>, dynamic>"), parenthesizedlambdaExpression.Clone());
                    
                    if (parentTarget.MemberName == "Range")
                    {
                         var identifierExpression = parentTarget.Target as IdentifierExpression;
                         if (identifierExpression != null && identifierExpression.Identifier == "Enumerable")
                         {
                             // support for Enumerable.Range(x, y).Select()

                             // convert Enumerable.Range(x, y) to Enumerable.Range(x, y).Cast<dynamic>()

                             var enumerableRange = parentInvocation.Clone();

                             var castToDynamic = new MemberReferenceExpression(enumerableRange, "Cast", new AstType[] { new SimpleType("dynamic") });

                            var dynamicEnumerableRange = new InvocationExpression(castToDynamic);

                             parentInvocation.ReplaceWith(dynamicEnumerableRange);
                         }
                    }
				}
			}
			return new CastExpression(new SimpleType("Func<dynamic, dynamic>"), parenthesizedlambdaExpression.Clone());
		}

		private static AstNode ModifyLambdaForSelectMany(LambdaExpression lambdaExpression,
		                                               ParenthesizedExpression parenthesizedlambdaExpression,
		                                               InvocationExpression invocationExpression)
		{
			AstNode node = lambdaExpression;
			if(invocationExpression.Arguments.Count > 0 && invocationExpression.Arguments.ElementAt(0) == lambdaExpression)// first one, select the collection
			{
				string type;
				if (ShouldSkipCastingToDynamicEnumerable(lambdaExpression.Body, out type))
				{
					return node;
				}
				// need to enter a cast for (IEnumerable<dynamic>) on the end of the lambda body
				var selectManyExpression = new LambdaExpression
				{
					Body =
						new CastExpression(new SimpleType("IEnumerable<dynamic>"), 
						                   new ParenthesizedExpression((Expression)lambdaExpression.Body.Clone())),
				};
				selectManyExpression.Parameters.AddRange(lambdaExpression.Parameters.Select(x=>(ParameterDeclaration)x.Clone()));

				node = new CastExpression(new SimpleType("Func<dynamic, IEnumerable<dynamic>>"),
				                          new ParenthesizedExpression(selectManyExpression.Clone()));
			}
			else if (invocationExpression.Arguments.Count > 1 && invocationExpression.Arguments.ElementAt(1) == lambdaExpression)// first one, select the collection
			{
				var parentLambda = invocationExpression.Arguments.ElementAt(0) as LambdaExpression;
				if (parentLambda != null)
				{
					string type;
					if (ShouldSkipCastingToDynamicEnumerable(parentLambda.Body, out type))
					{
						return new CastExpression(new SimpleType("Func<dynamic, " + type + ", dynamic>"), parenthesizedlambdaExpression.Clone());
					}
				}
				node = new CastExpression(new SimpleType("Func<dynamic, dynamic, dynamic>"), parenthesizedlambdaExpression.Clone());
			}
			return node;
		}

		private static bool ShouldSkipCastingToDynamicEnumerable(AstNode body, out string type)
		{
			type = null;

			var invocationExpression = body as InvocationExpression;
			if (invocationExpression == null)
				return false;
			var memberReferenceExpression = invocationExpression.Target as MemberReferenceExpression;
			if (memberReferenceExpression == null)
				return false;

			if (memberReferenceExpression.MemberName != "Range")
				return false;

			type = "int";

			var identifierExpression = memberReferenceExpression.Target as IdentifierExpression;
			if (identifierExpression != null)
				return identifierExpression.Identifier == "Enumerable";

			var targetReferenceExpression = memberReferenceExpression.Target as MemberReferenceExpression;

			return targetReferenceExpression.MemberName == "Enumerable";
		}

		private static AstNode ModifyLambdaForMinMax(LambdaExpression lambdaExpression,
		                                           ParenthesizedExpression parenthesizedlambdaExpression)
		{
			var node = new CastExpression(new SimpleType("Func<dynamic, IComparable>"), parenthesizedlambdaExpression.Clone());
			var castExpression = GetAsCastExpression(lambdaExpression.Body);
			if (castExpression != null)
			{
				var castToType = new SimpleType("Func", new SimpleType("dynamic"), castExpression.Type.Clone());
				node = new CastExpression(castToType, parenthesizedlambdaExpression.Clone());
			}
			return node;
		}

		private static CastExpression GetAsCastExpression(INode expressionBody)
		{
			var castExpression = expressionBody as CastExpression;
			if (castExpression != null)
				return castExpression;
			var parametrizedNode = expressionBody as ParenthesizedExpression;
			if (parametrizedNode != null)
				return GetAsCastExpression(parametrizedNode.Expression);
			return null;
		}

		private static AstNode ModifyLambdaForNumerics(LambdaExpression lambdaExpression,
		                                        ParenthesizedExpression parenthesizedlambdaExpression)
		{
			var castExpression = GetAsCastExpression(lambdaExpression.Body);
			if (castExpression != null)
			{
				var castToType = new SimpleType("Func", new SimpleType("dynamic"), castExpression.Type.Clone());
				return new CastExpression(castToType, parenthesizedlambdaExpression.Clone());
			}
			var expression = new LambdaExpression
			{
				Body = new CastExpression(new PrimitiveType("decimal"), new ParenthesizedExpression((Expression)lambdaExpression.Body.Clone())),
			};
			expression.Parameters.AddRange(lambdaExpression.Parameters.Select(x=>(ParameterDeclaration)x.Clone()));

			return new CastExpression(new SimpleType("Func<dynamic, decimal>"),
			                          new ParenthesizedExpression(expression));

		}
	}
}