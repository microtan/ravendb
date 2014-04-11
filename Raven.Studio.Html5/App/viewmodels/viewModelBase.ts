import appUrl = require("common/appUrl");
import database = require("models/database");
import filesystem = require("models/filesystem/filesystem");
import router = require("plugins/router");
import app = require("durandal/app");
import viewSystemDatabaseConfirm = require("viewmodels/viewSystemDatabaseConfirm");

/*
 * Base view model class that provides basic view model services, such as tracking the active database and providing a means to add keyboard shortcuts.
*/
class viewModelBase {
    public activeDatabase = ko.observable<database>().subscribeTo("ActivateDatabase", true);
    public activeFilesystem = ko.observable<filesystem>().subscribeTo("ActivateFilesystem", true);

    private keyboardShortcutDomContainers: string[] = [];
    private modelPollingHandle: number;
    static dirtyFlag = new ko.DirtyFlag([]);
    private static isConfirmedUsingSystemDatabase: boolean;

    /*
     * Called by Durandal when checking whether this navigation is allowed. 
     * Possible return values: boolean, promise<boolean>, {redirect: 'some/other/route'}, promise<{redirect: 'some/other/route'}>
     * 
     * We use this to determine whether we should allow navigation to a system DB page.
     * p.s. from Judah: a big scary prompt when loading the system DB is a bit heavy-handed, no? 
     */
    canActivate(args: any): any {
        var database = (appUrl.getDatabase()!=null) ? appUrl.getDatabase() : appUrl.getSystemDatabase(); //TODO: temporary fix for routing problem for system databse - remove this when fixed

        if (database.isSystem) {
            if (viewModelBase.isConfirmedUsingSystemDatabase) {
                return true;
            }

            return this.promptNavSystemDb();
        }

        viewModelBase.isConfirmedUsingSystemDatabase = false;
        return true;
    }

    /*
    * Called by Durandal when the view model is loaded and before the view is inserted into the DOM.
    */
    activate(args) {
        var db = appUrl.getDatabase();
        var currentDb = this.activeDatabase();
        if (!!db && db !== null && (!currentDb || currentDb.name !== db.name)) {
            ko.postbox.publish("ActivateDatabaseWithName", db.name);
        }

        var fs = appUrl.getFilesystem();
        var currentFilesystem = this.activeFilesystem();
        if (!currentFilesystem || currentFilesystem.name !== fs.name) {
            ko.postbox.publish("ActivateFilesystemWithName", fs.name);
        }

        this.modelPollingStart();

        window.onbeforeunload = (e: any) => {
            this.saveInObservable();
            var isDirty = viewModelBase.dirtyFlag().isDirty();
            if (isDirty) {
                var message = "You have unsaved data.";
                e = e || window.event;
                // For IE and Firefox
                if (e) {
                    e.returnValue = message;
                }
                // For Safari
                return message;
            }
            return null;
        };

        ko.postbox.publish("SetRawJSONUrl", "");
    }

    // Called back after the entire composition has finished (parents and children included)
    compositionComplete() {
        // Resync Changes
        viewModelBase.dirtyFlag().reset();
    }

    /*
    * Called by Durandal before deactivate in order to determine whether removing from the DOM is necessary.
    */
    canDeactivate(isClose): any {
        var isDirty = viewModelBase.dirtyFlag().isDirty();
        if (isDirty) {
            return app.showMessage('You have unsaved data. Are you sure you want to close?', 'Unsaved Data', ['Yes', 'No']);
        }
        return true;
    }

    /*
     * Called by Durandal when the view model is unloading and the view is about to be removed from the DOM.
     */
    deactivate() {
        this.activeDatabase.unsubscribeFrom("ActivateDatabase");
        this.activeFilesystem.unsubscribeFrom("ActivateFilesystem");
        this.keyboardShortcutDomContainers.forEach(el => this.removeKeyboardShortcuts(el));
        this.modelPollingStop();
    }

    /*
     * Creates a keyboard shortcut local to the specified element and its children.
     * The shortcut will be removed as soon as the view model is deactivated.
     * Also defines shortcut for ace editor, if ace editor was received
     */
    createKeyboardShortcut(keys: string, handler: () => void, elementSelector: string) {
        jwerty.key(keys, e => {
            e.preventDefault();
            handler();
        }, this, elementSelector);

        if (!this.keyboardShortcutDomContainers.contains(elementSelector)) {
            this.keyboardShortcutDomContainers.push(elementSelector);
        }
    }

    //A method to save the current value in the observables from text boxes and inputs before a refresh/page close.
    //Should be implemented on the inheriting class.
    saveInObservable() {

    }

    private removeKeyboardShortcuts(elementSelector: string) {
        $(elementSelector).unbind('keydown.jwerty');
    }

    /*
     * Navigates to the specified URL, recording a navigation event in the browser's history.
     */
    navigate(url: string) {
        router.navigate(url);
    }

    /*
     * Navigates by replacing the current URL. It does not record a new entry in the browser's navigation history.
     */
    updateUrl(url: string) {
        var options: DurandalNavigationOptions = {
            replace: true,
            trigger: false
        };
        router.navigate(url, options);
    }

    modelPolling() {
    }

    forceModelPolling() {
        this.modelPolling();
    }

    private modelPollingStart() {
        this.modelPolling();
        this.activeDatabase.subscribe(() => this.forceModelPolling());
        this.activeFilesystem.subscribe(() => this.forceModelPolling());
    }

    private modelPollingStop() {
        clearInterval(this.modelPollingHandle);
    }

    private promptNavSystemDb(): any {
        if (!appUrl.warnWhenUsingSystemDatabase) {
            return true;
        }

        var canNavTask = $.Deferred<any>();

        var systemDbConfirm = new viewSystemDatabaseConfirm();
        systemDbConfirm.viewTask
            .fail(() => canNavTask.resolve({ redirect: 'databases' }))
            .done(() => {
                viewModelBase.isConfirmedUsingSystemDatabase = true;
                canNavTask.resolve(true);
            });
        app.showDialog(systemDbConfirm);

        return canNavTask;
    }

}

export = viewModelBase;