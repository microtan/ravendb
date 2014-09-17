import commandBase = require("commands/commandBase");
import patchDocument = require("models/patchDocument");
import database = require("models/database");

class getPatchesCommand extends commandBase {

    constructor(private db: database) {
        super();
    }

    execute(): JQueryPromise<Array<patchDocument>> {
        var args = {
            startsWith: "Studio/Patch/",
            exclude: null,
            start: 0,
            pageSize: 256
        };

        return this.query("/docs", args, this.db, (dtos: patchDto[]) => dtos.map(dto => new patchDocument(dto)));
    }
}

export = getPatchesCommand;