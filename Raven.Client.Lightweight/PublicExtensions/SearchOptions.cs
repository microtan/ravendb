using System;

namespace Raven.Client
{
	[Flags]
	public enum SearchOptions
	{
		Or = 1,
		And = 2,
		Not = 4,
		Guess = 8
	}

	public enum EscapeQueryOptions
	{
		EscapeAll,
		AllowPostfixWildcard,
		/// <summary>
		/// This allows queries such as Name:*term*, which tend to be much
		/// more expensive and less performant than any other queries. 
		/// Consider carefully whatever you really need this, as there are other
		/// alternative for searching without doing extremely expensive leading 
		/// wildcard matches.
		/// </summary>
		AllowAllWildcards,
		RawQuery
	}
}