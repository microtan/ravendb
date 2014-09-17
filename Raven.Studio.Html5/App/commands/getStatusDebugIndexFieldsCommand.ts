import commandBase = require("commands/commandBase");
import database = require("models/database");
import appUrl = require("common/appUrl");

class getStatusDebugIndexFieldsCommand extends commandBase {

    constructor(private db: database, private indexStr: string) {
        super();
    }

    execute(): JQueryPromise<statusDebugIndexFieldsDto> {
        var url = "/debug/index-fields";
        return this.post(url, this.indexStr, this.db);
    }
}

export = getStatusDebugIndexFieldsCommand;