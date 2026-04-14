namespace HappyGymStats.Visualizer

/// Pure binning module — converts StatRecord lists into a surface grid (z-matrix).
module SurfaceBinner =

    /// Upper bound on axis resolution for large datasets.
    /// If the number of distinct axis values exceeds this, we bucketize into bin centers.
    ///
    /// Fewer bins = denser-looking surface (fewer empty cells), but less detail.
    let private maxBins = 25

    let private statPerEnergy (r: StatRecord) : float option =
        if r.EnergyUsed > 0.0 then Some (r.StatIncreased / r.EnergyUsed) else None

    /// Create quantile-based bins and a function that maps a value into [0..binCount-1].
    /// Quantile binning produces denser grids than uniform-width binning when values are heavily skewed.
    let private makeQuantileBins (values: float list) (requestedBinCount: int) : float list * (float -> int) =
        if values.IsEmpty then
            ([], fun _ -> 0)
        else
            let sorted = values |> List.sort
            let n = sorted.Length

            let pickAt (p: float) =
                // p in [0..1]
                let idx = int (p * float (n - 1))
                sorted.[max 0 (min (n - 1) idx)]

            let requestedBinCount = max 1 requestedBinCount

            let edgesRaw =
                [ 0 .. requestedBinCount ]
                |> List.map (fun i ->
                    let p = float i / float requestedBinCount
                    pickAt p)

            // Collapse duplicate edges; this effectively reduces the number of bins for low-cardinality data.
            let edges =
                edgesRaw
                |> List.distinct

            if edges.Length <= 1 then
                // All values identical
                ([ edgesRaw.Head ], fun _ -> 0)
            else
                let binCount = edges.Length - 1

                let centers =
                    [ 0 .. binCount - 1 ]
                    |> List.map (fun i -> (edges.[i] + edges.[i + 1]) / 2.0)

                let idxOf (v: float) =
                    // Find the last edge <= v (linear scan is fine for <= 26 edges).
                    // Map it to a bin index; clamp so max value lands in last bin.
                    let mutable i = 0
                    while i + 1 < edges.Length && v >= edges.[i + 1] do
                        i <- i + 1
                    if i >= binCount then binCount - 1 else i

                (centers, idxOf)

    /// Build a z-matrix from record list given axis values and indexers.
    let private buildMatrix
        (records: StatRecord list)
        (xCount: int)
        (yCount: int)
        (xIndexOf: float -> int)
        (yIndexOf: float -> int)
        : float list list =

        let sums = Array2D.create yCount xCount 0.0
        let counts = Array2D.create yCount xCount 0

        for r in records do
            match statPerEnergy r with
            | None -> ()
            | Some z ->
                let xi = xIndexOf r.StatBefore
                let yi = yIndexOf r.HappyBeforeTrain
                sums.[yi, xi] <- sums.[yi, xi] + z
                counts.[yi, xi] <- counts.[yi, xi] + 1

        [ 0 .. yCount - 1 ]
        |> List.map (fun yi ->
            [ 0 .. xCount - 1 ]
            |> List.map (fun xi ->
                let c = counts.[yi, xi]
                if c = 0 then System.Double.NaN else (sums.[yi, xi] / float c)
            )
        )

    /// Bins a list of StatRecords into a surface grid.
    /// X-axis = StatBefore (exact distinct values for small datasets; bucketized for large).
    /// Y-axis = HappyBeforeTrain (exact distinct values for small datasets; bucketized for large).
    /// Z-value = mean(StatIncreased / EnergyUsed) for each (x, y) bin; NaN when the bin is empty.
    let binRecords (records: StatRecord list) : GridResult =
        let usable =
            records
            |> List.filter (fun r -> r.EnergyUsed > 0.0)

        if usable.IsEmpty then
            { XValues = []; YValues = []; ZMatrix = [] }
        else
            let xDistinct = usable |> List.map _.StatBefore |> List.distinct |> List.sort
            let yDistinct = usable |> List.map _.HappyBeforeTrain |> List.distinct |> List.sort

            // For small datasets, preserve the exact axis values (tests + predictable output).
            if xDistinct.Length <= maxBins && yDistinct.Length <= maxBins then
                let xIndex = xDistinct |> List.mapi (fun i v -> v, i) |> Map.ofList
                let yIndex = yDistinct |> List.mapi (fun i v -> v, i) |> Map.ofList

                let xIndexOf v = Map.find v xIndex
                let yIndexOf v = Map.find v yIndex

                let zMatrix = buildMatrix usable xDistinct.Length yDistinct.Length xIndexOf yIndexOf

                { XValues = xDistinct; YValues = yDistinct; ZMatrix = zMatrix }
            else
                // Quantile bucketization produces denser surfaces for skewed / wide-range inputs.
                let xCenters, xIndexOf = makeQuantileBins (usable |> List.map _.StatBefore) maxBins
                let yCenters, yIndexOf = makeQuantileBins (usable |> List.map _.HappyBeforeTrain) maxBins

                let zMatrix = buildMatrix usable xCenters.Length yCenters.Length xIndexOf yIndexOf

                { XValues = xCenters; YValues = yCenters; ZMatrix = zMatrix }
