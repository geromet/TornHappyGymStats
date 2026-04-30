namespace HappyGymStats.Visualizer

/// Discriminated union representing the four trainable gym stats.
type StatType =
    | Strength
    | Defense
    | Speed
    | Dexterity

/// A single parsed gym training record containing the key metrics.
type StatRecord = {
    StatType: StatType
    StatBefore: float
    StatTotalBefore: float
    HappyBeforeTrain: float
    StatIncreased: float
    EnergyUsed: float
}

/// Result of parsing a CSV file: successfully extracted records and any parse errors.
type ReadResult = {
    Records: StatRecord list
    ParseErrors: string list
}

/// Detailed stat record that preserves the log id (and optionally timestamp) for verification/diagnostics.
type StatRecordRow = {
    LogId: string
    Timestamp: int64 option
    StatType: StatType
    StatBefore: float
    StatTotalBefore: float
    HappyBeforeTrain: float
    StatIncreased: float
    EnergyUsed: float
}

/// Result of parsing a CSV file into detailed records.
type ReadResultDetailed = {
    Records: StatRecordRow list
    ParseErrors: string list
}
