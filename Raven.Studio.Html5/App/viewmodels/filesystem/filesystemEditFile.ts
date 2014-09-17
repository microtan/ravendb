﻿import app = require("durandal/app");
import router = require("plugins/router");
import appUrl = require("common/appUrl");
import ace = require("ace/ace");

import filesystem = require("models/filesystem/filesystem");
import pagedList = require("common/pagedList");
import getFileCommand = require("commands/filesystem/getFileCommand");
import updateFileMetadataCommand = require("commands/filesystem/updateFileMetadataCommand");
import pagedResultSet = require("common/pagedResultSet");
import viewModelBase = require("viewmodels/viewModelBase");
import virtualTable = require("widgets/virtualTable/viewModel");
import file = require("models/filesystem/file");
import fileMetadata = require("models/filesystem/fileMetadata");
import deleteItems = require("viewmodels/deleteItems");

class filesystemEditFile extends viewModelBase {

    fileName = ko.observable<string>();
    file = ko.observable<file>();
    metadata: KnockoutComputed<fileMetadata>;
    fileMetadataEditor: AceAjax.Editor;
    fileMetadataText = ko.observable<string>();
    isBusy = ko.observable(false);
    metaPropsToRestoreOnSave = [];

    static editFileSelector = "#editFileContainer";

    constructor() {
        super();

        // When we programmatically change the document text or meta text, push it into the editor.
        this.fileMetadataText.subscribe(() => this.updateFileEditorText());
        this.fileName.subscribe(x => this.loadFile(x));
    }

    activate(args) {
        super.activate(args);
        this.metadata = ko.computed(() => this.file() ? this.file().__metadata : null);

        if (args.id != null) {
            this.fileName(args.id);
        }

        this.metadata.subscribe((meta: fileMetadata) => this.metadataChanged(meta));
    }

    // Called when the view is attached to the DOM.
    attached() {
        this.initializeFileEditor();
        this.setupKeyboardShortcuts();
        this.focusOnEditor();
    }

    setupKeyboardShortcuts() {
        this.createKeyboardShortcut("alt+shift+del", () => this.deleteFile(), filesystemEditFile.editFileSelector);
    }

    initializeFileEditor() {
        // Startup the Ace editor with JSON syntax highlighting.
        // TODO: Just use the simple binding handler instead.
        this.fileMetadataEditor = ace.edit("fileMetadataEditor");
        this.fileMetadataEditor.setTheme("ace/theme/xcode");
        this.fileMetadataEditor.setFontSize("16px");
        this.fileMetadataEditor.getSession().setMode("ace/mode/json");
        $("#fileMetadataEditor").on('blur', ".ace_text-input", () => this.storeFileEditorTextIntoObservable());
        this.updateFileEditorText();
    }

    focusOnEditor() {
        this.fileMetadataEditor.focus();
    }

    updateFileEditorText() {
        if (this.fileMetadataEditor) {
            this.fileMetadataEditor.getSession().setValue(this.fileMetadataText());
        }
    }

    storeFileEditorTextIntoObservable() {
        if (this.fileMetadataEditor) {
            var editorText = this.fileMetadataEditor.getSession().getValue();
            this.fileMetadataText(editorText);
        }
    }

    loadFile(fileName: string) {
        new getFileCommand(this.activeFilesystem(), fileName)
            .execute()
            .done((result: file) => this.file(result));
    }

    navigateToFiles() {
        var filesUrl = appUrl.forFilesystemFiles(this.activeFilesystem());
        router.navigate(filesUrl);
    }

    saveFileMetadata() {
        //the name of the document was changed and we have to save it as a new one
        var meta = JSON.parse(this.fileMetadataText());
        var currentDocumentId = this.fileName();

        this.metaPropsToRestoreOnSave.forEach(p => meta[p.name] = p.value);

        var saveCommand = new updateFileMetadataCommand(this.fileName(), meta, this.activeFilesystem(), true);
        var saveTask = saveCommand.execute();
        saveTask.done(() => {
            this.dirtyFlag().reset(); // Resync Changes

            this.loadFile(this.fileName());
        });
    }

    downloadFile() {
        var url = appUrl.forResourceQuery(this.activeFilesystem()) + "/files/" + this.fileName();
        window.location.assign(url);
    }

    refreshFile() {
        this.loadFile(this.fileName());
    }

    saveInObservable() { //TODO: remove this and use ace binding handler
        this.storeFileEditorTextIntoObservable();
    }

    deleteFile() {
        var file = this.file();
        if (file) {
            var viewModel = new deleteItems([file]);
            viewModel.deletionTask.done(() => {
                var filesUrl = appUrl.forFilesystemFiles(this.activeFilesystem());
                router.navigate(filesUrl);
            });
            app.showDialog(viewModel, filesystemEditFile.editFileSelector);
        }

        this.dirtyFlag().reset(); // Resync Changes
    }

    metadataChanged(meta: fileMetadata) {
        if (meta) {
            //this.metaPropsToRestoreOnSave.length = 0;
            var metaDto = this.metadata().toDto();

            // We don't want to show certain reserved properties in the metadata text area.
            // Remove them from the DTO, restore them on save.
            var metaPropsToRemove = ["Origin", "Raven-Server-Build", "Raven-Client-Version", "Non-Authoritative-Information", "Raven-Timer-Request",
                "Raven-Authenticated-User", "Raven-Last-Modified", "Has-Api-Key", "Access-Control-Allow-Origin", "Access-Control-Max-Age", "Access-Control-Allow-Methods",
                "Access-Control-Request-Headers", "Access-Control-Allow-Headers", "Reverse-Via", "Persistent-Auth", "Allow", "Content-Disposition", "Content-Encoding",
                "Content-Language", "Content-Location", "Content-MD5", "Content-Range", "Content-Type", "Expires", "Last-Modified", "Content-Length", "Keep-Alive", "X-Powered-By",
                "X-AspNet-Version", "X-Requested-With", "X-SourceFiles", "Accept-Charset", "Accept-Encoding", "Accept", "Accept-Language", "Authorization", "Cookie", "Expect",
                "From", "Host", "If-MatTemp-Index-Scorech", "If-Modified-Since", "If-None-Match", "If-Range", "If-Unmodified-Since", "Max-Forwards", "Referer", "TE", "User-Agent", "Accept-Ranges",
                "Age", "Allow", "ETag", "Location", "Retry-After", "Server", "Set-Cookie2", "Set-Cookie", "Vary", "Www-Authenticate", "Cache-Control", "Connection", "Date", "Pragma",
                "Trailer", "Transfer-Encoding", "Upgrade", "Via", "Warning", "X-ARR-LOG-ID", "X-ARR-SSL", "X-Forwarded-For", "X-Original-URL",
                "RavenFS-Size", "Temp-Request-Time", "DNT"];

            for (var property in metaDto) {
                if (metaDto.hasOwnProperty(property) && metaPropsToRemove.contains(property)) {
                    if (metaDto[property]) {
                        this.metaPropsToRestoreOnSave.push({ name: property, value: metaDto[property].toString() });
                    }
                    delete metaDto[property];
                }
            }

            var metaString = this.stringify(metaDto);
            this.fileMetadataText(metaString);
        }
    }

    private stringify(obj: any) {
        var prettifySpacing = 4;
        return JSON.stringify(obj, null, prettifySpacing);
    }
}

export = filesystemEditFile;