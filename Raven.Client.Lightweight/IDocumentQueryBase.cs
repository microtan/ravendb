using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using Raven.Abstractions.Data;
using Raven.Abstractions.Indexing;
using Raven.Client.Document;

namespace Raven.Client
{
	/// <summary>
	/// A query against a Raven index
	/// </summary>
	public interface IDocumentQueryBase<T, out TSelf>
		where TSelf : IDocumentQueryBase<T, TSelf>
	{
		/// <summary>
		/// Gets the document convention from the query session
		/// </summary>
		DocumentConvention DocumentConvention { get; }

		/// <summary>
		///   Negate the next operation
		/// </summary>
		TSelf Not { get; }

		/// <summary>
		///   Negate the next operation
		/// </summary>
		void NegateNext();

		/// <summary>
		///   Includes the specified path in the query, loading the document specified in that path
		/// </summary>
		/// <param name = "path">The path.</param>
		TSelf Include(string path);

		/// <summary>
		///   This function exists solely to forbid in memory where clause on IDocumentQuery, because
		///   that is nearly always a mistake.
		/// </summary>
		[Obsolete(
            @"
You cannot issue an in memory filter - such as Where(x=>x.Name == ""Ayende"") - on IDocumentQuery. 
This is likely a bug, because this will execute the filter in memory, rather than in RavenDB.
Consider using session.Query<T>() instead of session.DocumentQuery<T>. The session.Query<T>() method fully supports Linq queries, while session.DocumentQuery<T>() is intended for lower level API access.
If you really want to do in memory filtering on the data returned from the query, you can use: session.DocumentQuery<T>().ToList().Where(x=>x.Name == ""Ayende"")
"
            , true)]
		IEnumerable<T> Where(Func<T, bool> predicate);

		/// <summary>
		///   This function exists solely to forbid in memory where clause on IDocumentQuery, because
		///   that is nearly always a mistake.
		/// </summary>
		[Obsolete(
            @"
You cannot issue an in memory filter - such as Count(x=>x.Name == ""Ayende"") - on IDocumentQuery. 
This is likely a bug, because this will execute the filter in memory, rather than in RavenDB.
Consider using session.Query<T>() instead of session.DocumentQuery<T>. The session.Query<T>() method fully supports Linq queries, while session.DocumentQuery<T>() is intended for lower level API access.
If you really want to do in memory filtering on the data returned from the query, you can use: session.DocumentQuery<T>().ToList().Count(x=>x.Name == ""Ayende"")
"
            , true)]
		int Count(Func<T, bool> predicate);

		/// <summary>
		///   This function exists solely to forbid in memory where clause on IDocumentQuery, because
		///   that is nearly always a mistake.
		/// </summary>
		[Obsolete(
			@"
You cannot issue an in memory filter - such as Count() - on IDocumentQuery. 
This is likely a bug, because this will execute the filter in memory, rather than in RavenDB.
Consider using session.Query<T>() instead of session.DocumentQuery<T>. The session.Query<T>() method fully supports Linq queries, while session.DocumentQuery<T>() is intended for lower level API access.
If you really want to do in memory filtering on the data returned from the query, you can use: session.DocumentQuery<T>().ToList().Count()
"
			, true)]
		int Count();

		/// <summary>
		///   Includes the specified path in the query, loading the document specified in that path
		/// </summary>
		/// <param name = "path">The path.</param>
		TSelf Include(Expression<Func<T, object>> path);

		/// <summary>
		///   Takes the specified count.
		/// </summary>
		/// <param name = "count">The count.</param>
		/// <returns></returns>
		TSelf Take (int count);

		/// <summary>
		///   Skips the specified count.
		/// </summary>
		/// <param name = "count">The count.</param>
		/// <returns></returns>
		TSelf Skip(int count);

        /// <summary>
        ///   Returns first element or default value for type if sequence is empty.
        /// </summary>
        /// <returns></returns>
        T FirstOrDefault();

        /// <summary>
        ///   Returns first element or throws if sequence is empty.
        /// </summary>
        /// <returns></returns>
        T First();

        /// <summary>
        ///   Returns first element or default value for given type if sequence is empty. Throws if sequence contains more than one element.
        /// </summary>
        /// <returns></returns>
        T SingleOrDefault();

        /// <summary>
        ///   Returns first element or throws if sequence is empty or contains more than one element.
        /// </summary>
        /// <returns></returns>
        T Single();

		/// <summary>
		///   Filter the results from the index using the specified where clause.
		/// </summary>
		/// <param name = "whereClause">The where clause.</param>
		TSelf Where(string whereClause);

		/// <summary>
		///   Matches exact value
		/// </summary>
		/// <remarks>
		///   Defaults to NotAnalyzed
		/// </remarks>
		TSelf WhereEquals(string fieldName, object value);

		/// <summary>
		///   Matches exact value
		/// </summary>
		/// <remarks>
		///   Defaults to NotAnalyzed
		/// </remarks>
		TSelf WhereEquals<TValue>(Expression<Func<T, TValue>> propertySelector, TValue value);

		/// <summary>
		///   Matches exact value
		/// </summary>
		/// <remarks>
		///   Defaults to allow wildcards only if analyzed
		/// </remarks>
		TSelf WhereEquals(string fieldName, object value, bool isAnalyzed);

		/// <summary>
		///   Matches exact value
		/// </summary>
		/// <remarks>
		///   Defaults to allow wildcards only if analyzed
		/// </remarks>
		TSelf WhereEquals<TValue>(Expression<Func<T, TValue>> propertySelector, TValue value, bool isAnalyzed);

		/// <summary>
		/// Matches exact value
		/// </summary>
		TSelf WhereEquals(WhereParams whereParams);

		/// <summary>
		/// Check that the field has one of the specified value
		/// </summary>
		TSelf WhereIn(string fieldName, IEnumerable<object> values);

		/// <summary>
		/// Check that the field has one of the specified value
		/// </summary>
		TSelf WhereIn<TValue>(Expression<Func<T, TValue>> propertySelector, IEnumerable<TValue> values);

		/// <summary>
		///   Matches fields which starts with the specified value.
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereStartsWith (string fieldName, object value);

		/// <summary>
		///   Matches fields which starts with the specified value.
		/// </summary>
		/// <param name = "propertySelector">Property selector for the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereStartsWith<TValue>(Expression<Func<T, TValue>> propertySelector, TValue value);

		/// <summary>
		///   Matches fields which ends with the specified value.
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereEndsWith (string fieldName, object value);

		/// <summary>
		///   Matches fields which ends with the specified value.
		/// </summary>
		/// <param name = "propertySelector">Property selector for the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereEndsWith<TValue>(Expression<Func<T, TValue>> propertySelector, TValue value);

		/// <summary>
		///   Matches fields where the value is between the specified start and end, exclusive
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "start">The start.</param>
		/// <param name = "end">The end.</param>
		TSelf WhereBetween (string fieldName, object start, object end);

		/// <summary>
		///   Matches fields where the value is between the specified start and end, exclusive
		/// </summary>
		/// <param name = "propertySelector">Property selector for the field.</param>
		/// <param name = "start">The start.</param>
		/// <param name = "end">The end.</param>
		TSelf WhereBetween<TValue>(Expression<Func<T, TValue>> propertySelector, TValue start, TValue end);

		/// <summary>
		///   Matches fields where the value is between the specified start and end, inclusive
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "start">The start.</param>
		/// <param name = "end">The end.</param>
		TSelf WhereBetweenOrEqual (string fieldName, object start, object end);
		
		/// <summary>
		///   Matches fields where the value is between the specified start and end, inclusive
		/// </summary>
		/// <param name = "propertySelector">Property selector for the field.</param>
		/// <param name = "start">The start.</param>
		/// <param name = "end">The end.</param>
		TSelf WhereBetweenOrEqual<TValue>(Expression<Func<T, TValue>> propertySelector, TValue start, TValue end);

		/// <summary>
		///   Matches fields where the value is greater than the specified value
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereGreaterThan (string fieldName, object value);

		/// <summary>
		///   Matches fields where the value is greater than the specified value
		/// </summary>
		/// <param name = "propertySelector">Property selector for the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereGreaterThan<TValue>(Expression<Func<T, TValue>> propertySelector, TValue value);

		/// <summary>
		///   Matches fields where the value is greater than or equal to the specified value
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereGreaterThanOrEqual (string fieldName, object value);

		/// <summary>
		///   Matches fields where the value is greater than or equal to the specified value
		/// </summary>
		/// <param name = "propertySelector">Property selector for the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereGreaterThanOrEqual<TValue>(Expression<Func<T, TValue>> propertySelector, TValue value);

		/// <summary>
		///   Matches fields where the value is less than the specified value
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereLessThan (string fieldName, object value);

		/// <summary>
		///   Matches fields where the value is less than the specified value
		/// </summary>
		/// <param name = "propertySelector">Property selector for the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereLessThan<TValue>(Expression<Func<T, TValue>> propertySelector, TValue value);

		/// <summary>
		///   Matches fields where the value is less than or equal to the specified value
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereLessThanOrEqual (string fieldName, object value);

		/// <summary>
		///   Matches fields where the value is less than or equal to the specified value
		/// </summary>
		/// <param name = "propertySelector">Property selector for the field.</param>
		/// <param name = "value">The value.</param>
		TSelf WhereLessThanOrEqual<TValue>(Expression<Func<T, TValue>> propertySelector, TValue value);

		/// <summary>
		///   Add an AND to the query
		/// </summary>
		TSelf AndAlso ();

		/// <summary>
		///   Add an OR to the query
		/// </summary>
		TSelf OrElse();

		/// <summary>
		///   Specifies a boost weight to the last where clause.
		///   The higher the boost factor, the more relevant the term will be.
		/// </summary>
		/// <param name = "boost">boosting factor where 1.0 is default, less than 1.0 is lower weight, greater than 1.0 is higher weight</param>
		/// <returns></returns>
		/// <remarks>
		///   http://lucene.apache.org/java/2_4_0/queryparsersyntax.html#Boosting%20a%20Term
		/// </remarks>
		TSelf Boost (decimal boost);

		/// <summary>
		///   Specifies a fuzziness factor to the single word term in the last where clause
		/// </summary>
		/// <param name = "fuzzy">0.0 to 1.0 where 1.0 means closer match</param>
		/// <returns></returns>
		/// <remarks>
		///   http://lucene.apache.org/java/2_4_0/queryparsersyntax.html#Fuzzy%20Searches
		/// </remarks>
		TSelf Fuzzy (decimal fuzzy);

		/// <summary>
		///   Specifies a proximity distance for the phrase in the last where clause
		/// </summary>
		/// <param name = "proximity">number of words within</param>
		/// <returns></returns>
		/// <remarks>
		///   http://lucene.apache.org/java/2_4_0/queryparsersyntax.html#Proximity%20Searches
		/// </remarks>
		TSelf Proximity (int proximity);

		/// <summary>
		///   Filter matches to be inside the specified radius
		/// </summary>
		/// <param name = "radius">The radius.</param>
		/// <param name = "latitude">The latitude.</param>
		/// <param name = "longitude">The longitude.</param>
        /// <param name = "radiusUnits">The units of the <paramref name="radius"/>.</param>
        TSelf WithinRadiusOf(double radius, double latitude, double longitude, SpatialUnits radiusUnits = SpatialUnits.Kilometers);

		/// <summary>
		///   Filter matches to be inside the specified radius
		/// </summary>
		/// <param name="fieldName">The field name for the radius</param>
		/// <param name = "radius">The radius.</param>
		/// <param name = "latitude">The latitude.</param>
		/// <param name = "longitude">The longitude.</param>
        /// <param name = "radiusUnits">The units of the <paramref name="radius"/>.</param>
        TSelf WithinRadiusOf(string fieldName, double radius, double latitude, double longitude, SpatialUnits radiusUnits = SpatialUnits.Kilometers);

		/// <summary>
		/// Filter matches based on a given shape - only documents with the shape defined in fieldName that
		/// have a relation rel with the given shapeWKT will be returned
		/// </summary>
		/// <param name="fieldName">The name of the field containing the shape to use for filtering</param>
		/// <param name="shapeWKT">The query shape</param>
		/// <param name="rel">Spatial relation to check</param>
		/// <param name="distanceErrorPct">The allowed error percentage</param>
		/// <returns></returns>
		TSelf RelatesToShape(string fieldName, string shapeWKT, SpatialRelation rel, double distanceErrorPct = 0.025);

		/// <summary>
		///   Sorts the query results by distance.
		/// </summary>
		TSelf SortByDistance();

		/// <summary>
		///   Order the results by the specified fields
		///   The fields are the names of the fields to sort, defaulting to sorting by ascending.
		///   You can prefix a field name with '-' to indicate sorting by descending or '+' to sort by ascending
		/// </summary>
		/// <param name = "fields">The fields.</param>
		TSelf OrderBy(params string[] fields);
		
		/// <summary>
		///   Order the results by the specified fields
		///   The fields are the names of the fields to sort, defaulting to sorting by ascending.
		///   You can prefix a field name with '-' to indicate sorting by descending or '+' to sort by ascending
		/// </summary>
		/// <param name = "propertySelectors">Property selectors for the fields.</param>
		TSelf OrderBy<TValue>(params Expression<Func<T, TValue>>[] propertySelectors);

		/// <summary>
		///   Order the results by the specified fields
		///   The fields are the names of the fields to sort, defaulting to sorting by descending.
		///   You can prefix a field name with '-' to indicate sorting by descending or '+' to sort by ascending
		/// </summary>
		/// <param name = "fields">The fields.</param>
		TSelf OrderByDescending(params string[] fields);

		/// <summary>
		///   Order the results by the specified fields
		///   The fields are the names of the fields to sort, defaulting to sorting by descending.
		///   You can prefix a field name with '-' to indicate sorting by descending or '+' to sort by ascending
		/// </summary>
		/// <param name = "propertySelectors">Property selectors for the fields.</param>
		TSelf OrderByDescending<TValue>(params Expression<Func<T, TValue>>[] propertySelectors);

		/// <summary>
		///   Adds matches highlighting for the specified field.
		/// </summary>
		/// <remarks>
		///   The specified field should be analysed and stored for highlighter to work.
		///   For each match it creates a fragment that contains matched text surrounded by highlighter tags.
		/// </remarks>
		/// <param name="fieldName">The field name to highlight.</param>
		/// <param name="fragmentLength">The fragment length.</param>
		/// <param name="fragmentCount">The maximum number of fragments for the field.</param>
		/// <param name="fragmentsField">The field in query results item to put highlightings into.</param>
		TSelf Highlight(string fieldName, int fragmentLength, int fragmentCount, string fragmentsField);

		/// <summary>
		///   Adds matches highlighting for the specified field.
		/// </summary>
		/// <remarks>
		///   The specified field should be analysed and stored for highlighter to work.
		///   For each match it creates a fragment that contains matched text surrounded by highlighter tags.
		/// </remarks>
		/// <param name="fieldName">The field name to highlight.</param>
		/// <param name="fragmentLength">The fragment length.</param>
		/// <param name="fragmentCount">The maximum number of fragments for the field.</param>
		TSelf Highlight(string fieldName, int fragmentLength, int fragmentCount, out FieldHighlightings highlightings);

		/// <summary>
		///   Adds matches highlighting for the specified field.
		/// </summary>
		/// <remarks>
		///   The specified field should be analysed and stored for highlighter to work.
		///   For each match it creates a fragment that contains matched text surrounded by highlighter tags.
		/// </remarks>
		/// <param name="propertySelector">The property to highlight.</param>
		/// <param name="fragmentLength">The fragment length.</param>
		/// <param name="fragmentCount">The maximum number of fragments for the field.</param>
		/// <param name="fragmentsPropertySelector">The property to put highlightings into.</param>
		TSelf Highlight<TValue>(
			Expression<Func<T, TValue>> propertySelector, 
			int fragmentLength, 
			int fragmentCount,
			Expression<Func<T, IEnumerable>> fragmentsPropertySelector);
		
		/// <summary>
		///   Adds matches highlighting for the specified field.
		/// </summary>
		/// <remarks>
		///   The specified field should be analysed and stored for highlighter to work.
		///   For each match it creates a fragment that contains matched text surrounded by highlighter tags.
		/// </remarks>
		/// <param name="propertySelector">The property to highlight.</param>
		/// <param name="fragmentLength">The fragment length.</param>
		/// <param name="fragmentCount">The maximum number of fragments for the field.</param>
		TSelf Highlight<TValue>(
			Expression<Func<T, TValue>> propertySelector, 
			int fragmentLength, 
			int fragmentCount,
			out FieldHighlightings highlightings);

		/// <summary>
		///   Sets the tags to highlight matches with.
		/// </summary>
		/// <param name="preTag">Prefix tag.</param>
		/// <param name="postTag">Postfix tag.</param>
		TSelf SetHighlighterTags(string preTag, string postTag);

		/// <summary>
		///   Sets the tags to highlight matches with.
		/// </summary>
		/// <param name="preTags">Prefix tags.</param>
		/// <param name="postTags">Postfix tags.</param>
		TSelf SetHighlighterTags(string[] preTags, string[] postTags);

		/// <summary>
		///   Instructs the query to wait for non stale results as of now.
		/// </summary>
		/// <returns></returns>
		TSelf WaitForNonStaleResultsAsOfNow();

		/// <summary>
		/// Instructs the query to wait for non stale results as of the last write made by any session belonging to the 
		/// current document store.
		/// This ensures that you'll always get the most relevant results for your scenarios using simple indexes (map only or dynamic queries).
		/// However, when used to query map/reduce indexes, it does NOT guarantee that the document that this etag belong to is actually considered for the results. 
		/// </summary>
		TSelf WaitForNonStaleResultsAsOfLastWrite();

		/// <summary>
		/// Instructs the query to wait for non stale results as of the last write made by any session belonging to the 
		/// current document store.
		/// This ensures that you'll always get the most relevant results for your scenarios using simple indexes (map only or dynamic queries).
		/// However, when used to query map/reduce indexes, it does NOT guarantee that the document that this etag belong to is actually considered for the results. 
		/// </summary>
		TSelf WaitForNonStaleResultsAsOfLastWrite(TimeSpan waitTimeout);
		
		/// <summary>
		///   Instructs the query to wait for non stale results as of now for the specified timeout.
		/// </summary>
		/// <param name = "waitTimeout">The wait timeout.</param>
		/// <returns></returns>
		TSelf WaitForNonStaleResultsAsOfNow(TimeSpan waitTimeout);

		/// <summary>
		///   Instructs the query to wait for non stale results as of the cutoff date.
		/// </summary>
		/// <param name = "cutOff">The cut off.</param>
		/// <returns></returns>
		TSelf WaitForNonStaleResultsAsOf(DateTime cutOff);

		/// <summary>
		///   Instructs the query to wait for non stale results as of the cutoff date for the specified timeout
		/// </summary>
		/// <param name = "cutOff">The cut off.</param>
		/// <param name = "waitTimeout">The wait timeout.</param>
		TSelf WaitForNonStaleResultsAsOf(DateTime cutOff, TimeSpan waitTimeout);

		/// <summary>
		///   Instructs the query to wait for non stale results as of the cutoff etag.
		/// </summary>
		/// <param name = "cutOffEtag">The cut off etag.</param>
		/// <returns></returns>
		TSelf WaitForNonStaleResultsAsOf(Etag cutOffEtag);

		/// <summary>
		///   Instructs the query to wait for non stale results as of the cutoff etag for the specified timeout.
		/// </summary>
		/// <param name = "cutOffEtag">The cut off etag.</param>
		/// <param name = "waitTimeout">The wait timeout.</param>
		TSelf WaitForNonStaleResultsAsOf(Etag cutOffEtag, TimeSpan waitTimeout);

		/// <summary>
		///   EXPERT ONLY: Instructs the query to wait for non stale results.
		///   This shouldn't be used outside of unit tests unless you are well aware of the implications
		/// </summary>
		TSelf WaitForNonStaleResults();

		/// <summary>
		/// Allows you to modify the index query before it is sent to the server
		/// </summary>
		TSelf BeforeQueryExecution(Action<IndexQuery> beforeQueryExecution);

		/// <summary>
		///   EXPERT ONLY: Instructs the query to wait for non stale results for the specified wait timeout.
		///   This shouldn't be used outside of unit tests unless you are well aware of the implications
		/// </summary>
		/// <param name = "waitTimeout">The wait timeout.</param>
		TSelf WaitForNonStaleResults(TimeSpan waitTimeout);


		/// <summary>
		/// Order the search results randomly
		/// </summary>
		TSelf RandomOrdering();

		/// <summary>
		/// Order the search results randomly using the specified seed
		/// this is useful if you want to have repeatable random queries
		/// </summary>
		TSelf RandomOrdering(string seed);

		/// <summary>
		///   Adds an ordering for a specific field to the query
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "descending">if set to <c>true</c> [descending].</param>
		TSelf AddOrder(string fieldName, bool descending);

		/// <summary>
		///   Adds an ordering for a specific field to the query
		/// </summary>
		/// <param name = "propertySelector">Property selector for the field.</param>
		/// <param name = "descending">if set to <c>true</c> [descending].</param>
		TSelf AddOrder<TValue>(Expression<Func<T, TValue>> propertySelector, bool descending);

		/// <summary>
		///   Adds an ordering for a specific field to the query and specifies the type of field for sorting purposes
		/// </summary>
		/// <param name = "fieldName">Name of the field.</param>
		/// <param name = "descending">if set to <c>true</c> [descending].</param>
		/// <param name = "fieldType">the type of the field to be sorted.</param>
		TSelf AddOrder (string fieldName, bool descending, Type fieldType);

		/// <summary>
		///   Simplified method for opening a new clause within the query
		/// </summary>
		/// <returns></returns>
		TSelf OpenSubclause ();

		/// <summary>
		///   Simplified method for closing a clause within the query
		/// </summary>
		/// <returns></returns>
		TSelf CloseSubclause ();

		/// <summary>
		/// Perform a search for documents which fields that match the searchTerms.
		/// If there is more than a single term, each of them will be checked independently.
		/// </summary>
		TSelf Search(string fieldName, string searchTerms, EscapeQueryOptions escapeQueryOptions = EscapeQueryOptions.RawQuery);

		/// <summary>
		/// Perform a search for documents which fields that match the searchTerms.
		/// If there is more than a single term, each of them will be checked independently.
		/// </summary>
		TSelf Search<TValue>(Expression<Func<T, TValue>> propertySelector, string searchTerms, EscapeQueryOptions escapeQueryOptions = EscapeQueryOptions.RawQuery);

		/// <summary>
		/// Partition the query so we can intersect different parts of the query
		/// across different index entries.
		/// </summary>
		TSelf Intersect();

		/// <summary>
		/// Performs a query matching ANY of the provided values against the given field (OR)
		/// </summary>
		TSelf ContainsAny(string fieldName, IEnumerable<object> values);

		/// <summary>
		/// Performs a query matching ANY of the provided values against the given field (OR)
		/// </summary>
		TSelf ContainsAny<TValue>(Expression<Func<T, TValue>> propertySelector, IEnumerable<TValue> values);

		/// <summary>
		/// Performs a query matching ALL of the provided values against the given field (AND)
		/// </summary>
		TSelf ContainsAll(string fieldName, IEnumerable<object> values);

		/// <summary>
		/// Performs a query matching ALL of the provided values against the given field (AND)
		/// </summary>
		TSelf ContainsAll<TValue>(Expression<Func<T, TValue>> propertySelector, IEnumerable<TValue> values);

		/// <summary>
		/// Callback to get the results of the query
		/// </summary>
		void AfterQueryExecuted(Action<QueryResult> afterQueryExecuted);

		/// <summary>
		/// Called externally to raise the after query executed callback
		/// </summary>
		void InvokeAfterQueryExecuted(QueryResult result);

		/// <summary>
		/// Provide statistics about the query, such as total count of matching records
		/// </summary>
		TSelf Statistics(out RavenQueryStatistics stats);

		/// <summary>
		/// Select the default field to use for this query
		/// </summary>
		TSelf UsingDefaultField(string field);

		/// <summary>
		/// Select the default operator to use for this query
		/// </summary>
		TSelf UsingDefaultOperator(QueryOperator queryOperator);

		/// <summary>
		/// Disables tracking for queried entities by Raven's Unit of Work.
		/// Usage of this option will prevent holding query results in memory.
		/// </summary>
		TSelf NoTracking();

		/// <summary>
		/// Disables caching for query results.
		/// </summary>
		TSelf NoCaching();

        /// <summary>
        /// If set to true, this property will send multiple index entries from the same document (assuming the index project them)
        /// to the result transformer function. Otherwise, those entries will be consolidate an the transformer will be 
        /// called just once for each document in the result set
        /// </summary>
	    TSelf SetAllowMultipleIndexEntriesForSameDocumentToResultTransformer(bool val);

		/// <summary>
        /// Enables calculation of timings for various parts of a query (Lucene search, loading documents, transforming results). Default: false
		/// </summary>
		TSelf ShowTimings();

		/// <summary>
		/// Apply distinct operation to this query
		/// </summary>
		TSelf Distinct();
		
		/// <summary>
		/// Sets a transformer to use after executing a query
		/// </summary>
		/// <param name="resultsTransformer"></param>
		TSelf SetResultTransformer(string resultsTransformer);

		/// <summary>
		/// Adds an ordering by score for a specific field to the query
		/// </summary>
		TSelf OrderByScore();

		/// <summary>
		/// Adds an ordering by score for a specific field to the query
		/// </summary>
		TSelf OrderByScoreDescending();

		/// <summary>
		/// Adds explanations of scores calculated for queried documents to the query result
		/// </summary>
		TSelf ExplainScores();
	}
}