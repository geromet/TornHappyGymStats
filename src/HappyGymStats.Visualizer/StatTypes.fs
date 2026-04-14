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
    HappyBeforeTrain: float
    StatIncreased: float
    EnergyUsed: float
}

/// Result of parsing a CSV file: successfully extracted records and any parse errors.
type ReadResult = {
    Records: StatRecord list
    ParseErrors: string list
}

/// Result of surface grid binning: unique X/Y axis values and a z-matrix of mean (StatIncreased / EnergyUsed).
/// z.[yIdx].[xIdx] holds the mean stat-gain-per-energy for the (xVals.[xIdx], yVals.[yIdx]) bin.
/// Empty bins (no data points) are represented as NaN.
/// Note: Plotly.NET serializes NaN as the string "NaN" (JSON can't represent NaN). SurfacePlotter post-processes
/// the generated HTML to replace those "NaN" strings with null so Plotly.js renders holes correctly.
type GridResult = {
    XValues: float list      // X-axis values (either exact distinct StatBefore values or bin centers)
    YValues: float list      // Y-axis values (either exact distinct HappyBeforeTrain values or bin centers)
    ZMatrix: float list list // row-major: ZMatrix.[yIdx].[xIdx]
}
