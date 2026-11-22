using System.Windows;

namespace JacquardStudio;

/// <summary>Fits sampled freehand points with editable cubic Bézier segments.</summary>
/// <remarks>
/// Adapted from Paper.js PathFitter v0.12.18, based on Philip J. Schneider,
/// “An Algorithm for Automatically Fitting Digitized Curves,” Graphics Gems (1990).
/// Paper.js is distributed under the MIT license; see Docs/THIRD-PARTY-NOTICES.md.
/// The public tolerance is a maximum point-to-curve error in document pixels.
/// </remarks>
public static class BezierCurveFitter
{
    private const double Epsilon = 1e-12;
    private const int MaximumPoints = 12_000;

    private readonly record struct Cubic(Point Start, Point Control1, Point Control2, Point End);
    private readonly record struct Work(int First, int Last, Vector LeftTangent, Vector RightTangent);
    private readonly record struct ErrorPoint(double SquaredError, int Index);

    /// <summary>Fits an open point sequence into a path made of editable cubic Bézier segments.</summary>
    public static VectorDesignPath Fit(IReadOnlyList<Point> samples, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!double.IsFinite(tolerance) || tolerance is < 0.1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "The curve-fit tolerance must be between 0.1 and 1024 pixels.");
        if (samples.Count > MaximumPoints)
            throw new ArgumentOutOfRangeException(nameof(samples), $"A freehand path can contain at most {MaximumPoints} points.");

        var points = new List<Point>(samples.Count);
        foreach (Point sample in samples)
        {
            if (!double.IsFinite(sample.X) || !double.IsFinite(sample.Y))
                throw new ArgumentException("Curve-fit points must have finite coordinates.", nameof(samples));
            if (points.Count == 0 || Distance(points[^1], sample) > Epsilon)
                points.Add(sample);
        }
        if (points.Count < 2)
            throw new ArgumentException("At least two distinct points are required to fit a curve.", nameof(samples));

        double errorSquared = tolerance * tolerance;
        var fitted = new List<Cubic>(Math.Min(points.Count - 1, 256));
        var pending = new Stack<Work>();
        pending.Push(new Work(
            0,
            points.Count - 1,
            points[1] - points[0],
            points[^2] - points[^1]));

        // An explicit stack avoids deep recursive calls on very detailed strokes.
        while (pending.Count > 0)
        {
            Work work = pending.Pop();
            if (work.Last - work.First == 1)
            {
                Point start = points[work.First], end = points[work.Last];
                double handleLength = Distance(start, end) / 3;
                fitted.Add(new Cubic(
                    start,
                    start + Normalize(work.LeftTangent, handleLength),
                    end + Normalize(work.RightTangent, handleLength),
                    end));
                continue;
            }

            double[] parameters = ChordLengthParameterize(points, work.First, work.Last);
            double retryThreshold = Math.Max(errorSquared, errorSquared * errorSquared);
            int split = work.First + (work.Last - work.First) / 2;
            bool accepted = false;

            for (int iteration = 0; iteration <= 4; iteration++)
            {
                Cubic curve = GenerateBezier(points, work.First, work.Last, parameters, work.LeftTangent, work.RightTangent);
                ErrorPoint maximum = FindMaxError(points, work.First, work.Last, curve, parameters);
                split = maximum.Index;
                if (maximum.SquaredError < errorSquared)
                {
                    fitted.Add(curve);
                    accepted = true;
                    break;
                }
                if (maximum.SquaredError >= retryThreshold)
                    break;
                if (!Reparameterize(points, work.First, work.Last, parameters, curve))
                    break;
                retryThreshold = maximum.SquaredError;
            }
            if (accepted)
                continue;

            // findMaxError only chooses interior points. Keep a safe midpoint as a
            // guard against degenerate numerical input so each split makes progress.
            split = Math.Clamp(split, work.First + 1, work.Last - 1);
            Vector centerTangent = points[split - 1] - points[split + 1];
            if (centerTangent.LengthSquared <= Epsilon)
            {
                centerTangent = points[split - 1] - points[split];
                if (centerTangent.LengthSquared <= Epsilon)
                    centerTangent = points[split] - points[split + 1];
            }

            // The right half is pushed first so the output stays in path order.
            pending.Push(new Work(split, work.Last, -centerTangent, work.RightTangent));
            pending.Push(new Work(work.First, split, work.LeftTangent, centerTangent));
        }

        if (fitted.Count == 0)
            throw new InvalidOperationException("Curve fitting did not produce any segments.");

        var result = new VectorDesignPath();
        var first = VectorDesignDocument.Node(fitted[0].Start.X, fitted[0].Start.Y);
        result.Nodes.Add(first);
        foreach (Cubic curve in fitted)
        {
            VectorDesignNode from = result.Nodes[^1];
            from.HasOut = true;
            from.OutX = curve.Control1.X;
            from.OutY = curve.Control1.Y;
            from.CurveToNext = true;

            var to = VectorDesignDocument.Node(curve.End.X, curve.End.Y);
            to.HasIn = true;
            to.InX = curve.Control2.X;
            to.InY = curve.Control2.Y;
            result.Nodes.Add(to);
        }
        return result;
    }

    private static Cubic GenerateBezier(IReadOnlyList<Point> points, int first, int last, IReadOnlyList<double> parameters, Vector leftTangent, Vector rightTangent)
    {
        Point start = points[first], end = points[last];
        double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;
        for (int i = 0; i <= last - first; i++)
        {
            double u = parameters[i], t = 1 - u, b = 3 * u * t;
            double b1 = b * t, b2 = b * u, b3 = u * u * u;
            Vector a1 = Normalize(leftTangent, b1), a2 = Normalize(rightTangent, b2);
            Point endpointBlend = start + (end - start) * (b2 + b3);
            Vector residual = points[first + i] - endpointBlend;
            c00 += Vector.Multiply(a1, a1);
            c01 += Vector.Multiply(a1, a2);
            c11 += Vector.Multiply(a2, a2);
            x0 += Vector.Multiply(a1, residual);
            x1 += Vector.Multiply(a2, residual);
        }

        double determinant = c00 * c11 - c01 * c01;
        double alpha1, alpha2;
        if (Math.Abs(determinant) > Epsilon)
        {
            alpha1 = (x0 * c11 - x1 * c01) / determinant;
            alpha2 = (c00 * x1 - c01 * x0) / determinant;
        }
        else
        {
            double c0 = c00 + c01, c1 = c01 + c11;
            alpha1 = alpha2 = Math.Abs(c0) > Epsilon ? x0 / c0 : Math.Abs(c1) > Epsilon ? x1 / c1 : 0;
        }

        double chordLength = Distance(start, end), alphaEpsilon = Epsilon * chordLength;
        Vector? handle1 = null, handle2 = null;
        if (alpha1 < alphaEpsilon || alpha2 < alphaEpsilon)
        {
            alpha1 = alpha2 = chordLength / 3;
        }
        else
        {
            Vector line = end - start;
            handle1 = Normalize(leftTangent, alpha1);
            handle2 = Normalize(rightTangent, alpha2);
            if (Vector.Multiply(handle1.Value, line) - Vector.Multiply(handle2.Value, line) > chordLength * chordLength)
            {
                alpha1 = alpha2 = chordLength / 3;
                handle1 = handle2 = null;
            }
        }

        return new Cubic(
            start,
            start + (handle1 ?? Normalize(leftTangent, alpha1)),
            end + (handle2 ?? Normalize(rightTangent, alpha2)),
            end);
    }

    private static double[] ChordLengthParameterize(IReadOnlyList<Point> points, int first, int last)
    {
        var parameters = new double[last - first + 1];
        for (int i = first + 1; i <= last; i++)
            parameters[i - first] = parameters[i - first - 1] + Distance(points[i], points[i - 1]);
        double length = parameters[^1];
        if (length <= Epsilon)
        {
            for (int i = 0; i < parameters.Length; i++)
                parameters[i] = (double)i / (parameters.Length - 1);
        }
        else
        {
            for (int i = 1; i < parameters.Length; i++)
                parameters[i] /= length;
        }
        return parameters;
    }

    private static ErrorPoint FindMaxError(IReadOnlyList<Point> points, int first, int last, Cubic curve, IReadOnlyList<double> parameters)
    {
        int split = first + (last - first) / 2;
        double maximum = 0;
        for (int i = first + 1; i < last; i++)
        {
            Point fitted = Evaluate(curve, parameters[i - first]);
            double squared = (fitted - points[i]).LengthSquared;
            if (squared >= maximum)
            {
                maximum = squared;
                split = i;
            }
        }
        return new ErrorPoint(maximum, split);
    }

    private static bool Reparameterize(IReadOnlyList<Point> points, int first, int last, double[] parameters, Cubic curve)
    {
        for (int i = first; i <= last; i++)
        {
            double next = FindRoot(curve, points[i], parameters[i - first]);
            if (!double.IsFinite(next)) return false;
            parameters[i - first] = next;
        }
        for (int i = 1; i < parameters.Length; i++)
            if (parameters[i] <= parameters[i - 1] || parameters[i] is < 0 or > 1)
                return false;
        return parameters[^1] <= 1;
    }

    private static double FindRoot(Cubic curve, Point point, double u)
    {
        Vector[] firstDerivative =
        {
            (curve.Control1 - curve.Start) * 3,
            (curve.Control2 - curve.Control1) * 3,
            (curve.End - curve.Control2) * 3
        };
        Vector[] secondDerivative =
        {
            (firstDerivative[1] - firstDerivative[0]) * 2,
            (firstDerivative[2] - firstDerivative[1]) * 2
        };
        Vector difference = Evaluate(curve, u) - point;
        Vector derivative = Evaluate(firstDerivative, u);
        Vector second = Evaluate(secondDerivative, u);
        double denominator = Vector.Multiply(derivative, derivative) + Vector.Multiply(difference, second);
        return Math.Abs(denominator) <= Epsilon ? u : u - Vector.Multiply(difference, derivative) / denominator;
    }

    private static Point Evaluate(Cubic curve, double t)
    {
        Point a = Lerp(curve.Start, curve.Control1, t), b = Lerp(curve.Control1, curve.Control2, t), c = Lerp(curve.Control2, curve.End, t);
        return Lerp(Lerp(a, b, t), Lerp(b, c, t), t);
    }

    private static Vector Evaluate(IReadOnlyList<Vector> curve, double t)
    {
        var work = curve.ToArray();
        for (int level = 1; level < work.Length; level++)
            for (int i = 0; i < work.Length - level; i++)
                work[i] = work[i] * (1 - t) + work[i + 1] * t;
        return work[0];
    }

    private static Point Lerp(Point from, Point to, double amount) => from + (to - from) * amount;

    private static Vector Normalize(Vector value, double length = 1)
    {
        double magnitude = value.Length;
        return magnitude > Epsilon && double.IsFinite(magnitude) ? value * (length / magnitude) : new Vector();
    }

    private static double Distance(Point one, Point two) => (one - two).Length;
}
