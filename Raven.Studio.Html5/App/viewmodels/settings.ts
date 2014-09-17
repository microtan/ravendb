import durandalRouter = require("plugins/router");
import database = require("models/database");
import appUrl = require("common/appUrl");
import viewModelBase = require("viewmodels/viewModelBase");

class settings extends viewModelBase {

    router: DurandalRootRouter = null;
    appUrls: computedAppUrls;

    private bundleMap = { quotas: "Quotas", replication: "Replication", sqlreplication: "SQL Replication", versioning: "Versioning", periodicexport: "Periodic Export", scriptedindexresults: "Scripted Index" };
    private sqlSubBundles = ["sqlreplicationconnectionstringsmanagement", "editsqlreplication"];
    userDatabasePages = ko.observableArray(["Database Settings", "Custom Functions"]);
    activeSubViewTitle: KnockoutComputed<string>;

    constructor() {
        super();

        this.appUrls = appUrl.forCurrentDatabase();

        var databaseSettingsRoute = { route: ['databases/settings', 'databases/settings/databaseSettings'], moduleId: 'viewmodels/databaseSettings', title: 'Database Settings', nav: true, hash: appUrl.forCurrentDatabase().databaseSettings };
        var quotasRoute = { route: 'databases/settings/quotas', moduleId: 'viewmodels/quotas', title: 'Quotas', nav: true, hash: appUrl.forCurrentDatabase().quotas };
        var replicationsRoute = { route: 'databases/settings/replication', moduleId: 'viewmodels/replications', title: 'Replication', nav: true, hash: appUrl.forCurrentDatabase().replications };
        var sqlReplicationsRoute = { route: 'databases/settings/sqlReplication', moduleId: 'viewmodels/sqlReplications', title: 'SQL Replication', nav: true, hash: appUrl.forCurrentDatabase().sqlReplications };
        var editsqlReplicationsRoute = { route: 'databases/settings/editSqlReplication(/:sqlReplicationName)', moduleId: 'viewmodels/editSqlReplication', title: 'Edit SQL Replication', nav: true, hash: appUrl.forCurrentDatabase().editSqlReplication };
        var sqlReplicationsConnectionsRoute = { route: 'databases/settings/sqlReplicationConnectionStringsManagement', moduleId: 'viewmodels/sqlReplicationConnectionStringsManagement', title: 'SQL Replication Connection Strings', nav: true, hash: appUrl.forCurrentDatabase().sqlReplicationsConnections};
        var versioningRoute = { route: 'databases/settings/versioning', moduleId: 'viewmodels/versioning', title: 'Versioning', nav: true, hash: appUrl.forCurrentDatabase().versioning };
        var periodicExportRoute = { route: 'databases/settings/periodicExport', moduleId: 'viewmodels/periodicExport', title: 'Periodic Export', nav: true, hash: appUrl.forCurrentDatabase().periodicExport };
        //var scriptedIndexesRoute = { route: 'databases/settings/scriptedIndex', moduleId: 'viewmodels/scriptedIndexes', title: 'Scripted Index', nav: true, hash: appUrl.forCurrentDatabase().scriptedIndexes };
        var customFunctionsEditorRoute = { route: 'databases/settings/customFunctionsEditor', moduleId: 'viewmodels/customFunctionsEditor', title: 'Custom Functions', nav: true, hash: appUrl.forCurrentDatabase().customFunctionsEditor };

        this.router = durandalRouter.createChildRouter()
            .map([
                databaseSettingsRoute,
                quotasRoute,
                replicationsRoute,
                sqlReplicationsRoute,
                sqlReplicationsConnectionsRoute,
                editsqlReplicationsRoute,
                versioningRoute,
                periodicExportRoute,
                //scriptedIndexesRoute,
                customFunctionsEditorRoute
            ])
            .buildNavigationModel();

        this.router.guardRoute = (instance: Object, instruction: DurandalRouteInstruction) => this.getValidRoute(instance, instruction);

        appUrl.mapUnknownRoutes(this.router);

        this.activeSubViewTitle = ko.computed(() => {
            // Is there a better way to get the active route?
            var activeRoute = this.router.navigationModel().first(r=> r.isActive());
            return activeRoute != null ? activeRoute.title : "";
        });
    }

    /**
    * Checks whether the route can be navigated to. Returns true if it can be navigated to, or a redirect URI if it can't be navigated to.
    * This is used for preventing a navigating to system-only pages when the current databagse is non-system, and vice-versa.
    */
    getValidRoute(instance: Object, instruction: DurandalRouteInstruction): any {
        var db: database = this.activeDatabase();
        var pathArr = instruction.fragment.split('/');
        var bundelName = pathArr[pathArr.length - 1].toLowerCase();
        var isLegalBundelName = (this.bundleMap[bundelName] != undefined);
        var isBundleExists = this.userDatabasePages.indexOf(this.bundleMap[bundelName]) > -1;
        var isSqlSubBundle = this.sqlSubBundles.indexOf(bundelName) > -1;
        var isSqlBundleExists = this.userDatabasePages.indexOf("SQL Replication") > -1;
        
        if (db.isSystem) {
            return appUrl.forDocuments(null, db);
        }
        else if ((isLegalBundelName && isBundleExists == false) || (isSqlSubBundle && isSqlBundleExists == false)) {
            return appUrl.forCurrentDatabase().databaseSettings();
        }

        return true;
    }

    isEditingSqlReplication(navigationalModel: any, curNavHash: any) {
        var activeRoute = navigationalModel.first(r => r.isActive());
        if (!!activeRoute && !!curNavHash && !!activeRoute.hash) {
            return curNavHash.indexOf('databases/settings/sqlReplication') >= 0 &&
                (activeRoute.route.indexOf('databases/settings/editSqlReplication') >= 0 ||
                activeRoute.route.indexOf('databases/settings/sqlReplicationConnectionStringsManagement') >= 0);
        }
        return false;
    }

    activate(args) {
        super.activate(args);

        this.userDatabasePages(["Database Settings", "Custom Functions"]);
        var db: database = this.activeDatabase();
        var bundles: string[] = db.activeBundles();

        bundles.forEach((bundle: string) => {
            var bundleName = this.bundleMap[bundle.toLowerCase()];
            if (bundleName != undefined) {
                this.userDatabasePages.push(bundleName);
            }
        });
    }

    routeIsVisible(route: DurandalRouteConfiguration) {
        var bundleTitle = route.title;

        if (this.userDatabasePages.indexOf(bundleTitle) !== -1) {
            // Database Settings, Quotas, Replication, SQL Replication, Versioning, Periodic Export and Scripted Index are visible only when we're on a user database.
            return true;
        }

        return false;
    }
}

export = settings;
