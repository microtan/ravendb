import commandBase = require("commands/commandBase");
import database = require("models/database");

class getRunningTasksCommand extends commandBase {

    constructor(private db: database) {
        super();
    }

    execute(): JQueryPromise<runningTaskDto[]> {
        var url = "/operations";
        return this.query<runningTaskDto[]>(url, null, this.db);
    }
}

export = getRunningTasksCommand;