import commandBase = require("commands/commandBase");
import document = require("models/document");
import database = require("models/database");
import pagedResultSet = require("common/pagedResultSet");

class getSystemDocumentCommand extends commandBase {

    constructor(private id: string) {
        super();
    }

    execute(): JQueryPromise<pagedResultSet> {

        var deferred = $.Deferred();

        var url = "/docs/" + this.id;
        var docQuery = this.query(url, null, null);
        docQuery.done((dto: databaseDocumentDto) => deferred.resolve(dto));
        docQuery.fail(response => deferred.reject(response));

        return deferred;
    }
}

export = getSystemDocumentCommand;