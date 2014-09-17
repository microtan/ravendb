﻿import commandBase = require("commands/commandBase");
import filesystem = require("models/filesystem/filesystem");

class getFoldersStatsCommand extends commandBase {

    constructor(private fs: filesystem, private skip : number, private take: number, private directory?: string) {
        super();
    }

    execute(): JQueryPromise<folderNodeDto[]> {

        var url = "/folders/Subdirectories";
        if (this.directory) {
            url += "?directory="+this.directory;
        }
        var args = {
            start: this.skip,
            pageSize: this.take
        }
        
        return this.query<folderNodeDto[]>(url, args, this.fs, (result: string[]) => result.map((x: string) =>
        { 
            return {
                key: x,
                title: x.substring(x.lastIndexOf("/")+1),
                isLazy: true,
                isFolder: true
            }
        }));
    }
}

export = getFoldersStatsCommand;