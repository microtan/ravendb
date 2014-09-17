import commandBase = require("commands/commandBase");
import database = require("models/database");
import appUrl = require("common/appUrl");

class getStatusDebugMetricsCommand extends commandBase {

    constructor(private db: database) {
        super();
    }

    execute(): JQueryPromise<statusDebugMetricsDto> {
        var url = this.getQueryUrlFragment();
        return this.query<statusDebugMetricsDto>(url, null, this.db);
    }

    getQueryUrl(): string {
        return appUrl.forResourceQuery(this.db) + this.getQueryUrlFragment();
    }

    private getQueryUrlFragment(): string {
        return "/debug/metrics";
    }
}

export = getStatusDebugMetricsCommand;