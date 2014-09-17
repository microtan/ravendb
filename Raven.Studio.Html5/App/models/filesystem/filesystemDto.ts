enum filesystemSynchronizationType {
    Unknown = 0,
    ContentUpdate = 1,
    MetadataUpdate = 2,
    Rename = 3,
    Delete = 4,
}

interface filesystemSynchronizationDetailsDto {
    FileName: string;
    FileETag: string;
    DestinationUrl: string;
    Type: string;
    Direction: synchronizationDirection;
}

interface filesystemMetricsHistogramDataDto {
    Counter: number;
    Max: number;
    Min: number;
    Mean: number;
    Stdev: number;
}

interface filesystemMetricsMeterDataDto {
    Count: number;
    MeanRate: number;
    OneMinuteRate: number;
    FiveMinuteRate: number;
    FifteenMinuteRate: number;
}

interface filesystemMetricsDto {
    FilesWritesPerSecond: number;
    RequestsPerSecond: number;
    Requests: filesystemMetricsMeterDataDto;
    RequestsDuration: filesystemMetricsHistogramDataDto;
}

interface filesystemStatisticsDto {
    Name: string;
    FileCount: number;
    Metrics: filesystemMetricsDto;
    ActiveSyncs: filesystemSynchronizationDetailsDto[];
    PendingSyncs: filesystemSynchronizationDetailsDto[];
}

interface filesystemFileHeaderDto {
    Name: string;

    TotalSize?: number;
    UploadedSize: number;

    HumaneTotalSize: string;
    HumaneUploadedSize: string;

    Metadata: any;
}

interface fileMetadataDto {
    'Last-Modified'?: string;
    '@etag'?: string;
    'Raven-Synchronization-History': string;
    'Raven-Synchronization-Version': string;
    'Raven-Synchronization-Source': string;
    'RavenFS-Size': string;
    'Origin': string;
}

interface filesystemSearchResultsDto {
    Files: filesystemFileHeaderDto[];
    FileCount: number;
    Start: number;
    PageSize: number;
}

interface filesystemConfigSearchResultsDto {
    ConfigNames: string[];
    TotalCount: number;
    Start: number;
    PageSize: number;
}

interface filesystemHistoryItemDto {
    Version: number;
    ServerId: string;
}

interface filesystemConflictItemDto {
    FileName: string;
    RemoteServerUrl: string;

    RemoteHistory: filesystemHistoryItemDto[];
    CurrentHistory: filesystemHistoryItemDto[];
}

interface filesystemListPageDto<T> {
    TotalCount: number;
    Items: T[];
}

interface synchronizationReplicationsDto {
    Destinations: synchronizationDestinationDto[];
    Source: string;
}

interface synchronizationDestinationDto {
    ServerUrl: string;
    Username: string;
    Password: string;
    Domain: string;
    ApiKey: string;
    FileSystem: string;
    Enabled: boolean;
}

interface folderNodeDto {
    key: string;
    title: string;
    isLazy: boolean;
    isFolder: boolean
    addClass?: string;
}

interface synchronizationUpdateNotification {
    FileSystemName: string;
    FileName: string;
    DestinationFileSystemUrl: string;
    SourceServerId: string;
    SourceFileSystemUrl: string;
    Type: filesystemSynchronizationType;
    Action: synchronizationAction;
    Direction: synchronizationDirection;
}

enum synchronizationAction {
    Enqueue,
    Start,
    Finish
}

enum synchronizationDirection {
    Outgoing,
    Incoming
}

interface synchronizationConflictNotification {
    FileSystemName: string;
    FileName: string;
    SourceServerUrl: string;
    Status: conflictStatus;
    RemoteFileHeader: any;
}

enum conflictStatus {
    Detected = 0,
    Resolved = 1
}

interface fileChangeNotification {
    FileSystemName: string;
    File: string;
    Action: fileChangeAction;
}

enum fileChangeAction {
    Add,
    Delete,
    Update,
    Renaming,
    Renamed
}

interface filesystemConfigNotification {
    FileSystemName: string;
    Name: string;
    Action: filesystemConfigurationChangeAction;
}

enum filesystemConfigurationChangeAction {
    Set,
    Delete
}