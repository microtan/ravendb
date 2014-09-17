﻿import commandBase = require("commands/commandBase");
import file = require("models/filesystem/file");
import filesystem = require("models/filesystem/filesystem");
import pagedResultSet = require("common/pagedResultSet");

class getFilesystemFilesCommand extends commandBase {

    constructor(private fs: filesystem, private directory: string, private skip: number, private take: number) {
        super();
    }

    execute(): JQueryPromise<pagedResultSet> {
        var filesTask = this.fetchFiles();
        var doneTask = $.Deferred();

        filesTask.done((results: searchResults) => {
            var files = results.Files.map(d => new file(d, true));
            var totalCount = results.FileCount;
            doneTask.resolve(new pagedResultSet(files, totalCount));
        });
        filesTask.fail(xhr => doneTask.reject(xhr));

        return doneTask;
    }

    private fetchFiles(): JQueryPromise<file[]> {
        var level = 1;
        if (this.directory) {
            var slashMatches = this.directory.count("/");
            if (slashMatches) {
                level = level + slashMatches;
            }
        }

        var levelQuery = "__level:" + level;
        var args = {
            query: this.directory ? "__directoryName:" + this.directory + " AND " + levelQuery : levelQuery,
            start: this.skip,
            pageSize: this.take
        };

        var url = "/search";
        var task = this.query(url, args, this.fs, null);

        return task;
    }
}

export = getFilesystemFilesCommand;