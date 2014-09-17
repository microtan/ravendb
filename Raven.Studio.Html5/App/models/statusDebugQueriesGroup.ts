import document = require("models/document");
import conflictVersion = require("models/conflictVersion");
import statusDebugQueriesQuery = require("models/statusDebugQueriesQuery");

class statusDebugQueriesGroup {
    indexName: string;
    queries = ko.observableArray<statusDebugQueriesQuery>();

    constructor(dto: statusDebugQueriesGroupDto) {
        this.indexName = dto.IndexName;
        this.queries($.map(dto.Queries, q => new statusDebugQueriesQuery(q)));
    }
    
}

export = statusDebugQueriesGroup;