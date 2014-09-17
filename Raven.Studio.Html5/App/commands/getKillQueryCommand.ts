import commandBase = require("commands/commandBase");
import database = require("models/database");

class getKillQueryCommand extends commandBase {

    constructor(private db: database, private queryId: number) {
        super();
    }

    execute(): JQueryPromise<userInfoDto> {
        var url = "/admin/killQuery";
        var args = {
            id: this.queryId
        }
        return this.query<userInfoDto>(url, args, this.db)
            .fail((response: JQueryXHR) => this.reportError("Failed to kill query", response.responseText, response.statusText));
        
    }
}

export = getKillQueryCommand;