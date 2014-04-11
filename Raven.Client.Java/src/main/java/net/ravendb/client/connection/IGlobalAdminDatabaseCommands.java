package net.ravendb.client.connection;

import net.ravendb.abstractions.data.AdminStatistics;
import net.ravendb.abstractions.data.DatabaseDocument;


public interface IGlobalAdminDatabaseCommands {
  /**
   * Get admin statistics
   * @return
   */
  public AdminStatistics getStatistics();

  /**
   * Creates a database
   * @param databaseDocument
   */
  public void createDatabase(DatabaseDocument databaseDocument);

  /**
   * Deteles a database with the specified name
   * @param dbName
   */
  public void deleteDatabase(String dbName);

  /**
   * Deteles a database with the specified name
   * @param dbName
   * @param hardDelete
   */
  public void deleteDatabase(String dbName, boolean hardDelete);

  /**
   * Sends an async command to compact a database. During the compaction the specified database will be offline.
   * @param databaseName
   */
  public void compactDatabase(String databaseName);

  /**
   * Gets DatabaseCommands
   * @return
   */
  public IDatabaseCommands getCommands();

  /**
   * Begins a restore operation
   * @param restoreLocation
   * @param databaseLocation
   */
  public void startRestore(String restoreLocation, String databaseLocation);

  /**
   * Begins a restore operation
   * @param restoreLocation
   * @param databaseLocation
   */
  public void startRestore(String restoreLocation, String databaseLocation, String databaseName);

  /**
   * Begins a restore operation
   * @param restoreLocation
   * @param databaseLocation
   */
  public void startRestore(String restoreLocation, String databaseLocation, String databaseName, boolean defrag);


  /**
   * Begins a backup operation
   * @param backupLocation
   * @param databaseDocument
   */
  public void startBackup(String backupLocation, DatabaseDocument databaseDocument, String databaseName);

  /**
   * Ensures that the database exists, creating it if needed
   * @param name
   * @param ignoreFailures
   */
  public void ensureDatabaseExists(String name);

  /**
   * Ensures that the database exists, creating it if needed
   * @param name
   * @param ignoreFailures
   */
  public void ensureDatabaseExists(String name, boolean ignoreFailures);

}
