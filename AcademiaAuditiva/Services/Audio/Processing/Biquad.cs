namespace AcademiaAuditiva.Services.Audio.Processing;

/// <summary>A stable direct-form-I biquad with double-precision state for one channel.</summary>
internal sealed class Biquad
{
    private readonly double _b0, _b1, _b2, _a1, _a2;
    private double _x1, _x2, _y1, _y2;

    public Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;

        // The three denominator inequalities put both poles strictly inside the unit circle.
        if (!(a0 > 0 && double.IsFinite(a0)
            && double.IsFinite(_b0) && double.IsFinite(_b1) && double.IsFinite(_b2)
            && double.IsFinite(_a1) && double.IsFinite(_a2)
            && Math.Abs(_a2) < 1 && 1 + _a1 + _a2 > 0 && 1 - _a1 + _a2 > 0))
        {
            throw new ArgumentException("A biquad needs finite coefficients and poles strictly inside the unit circle.");
        }
    }

    public double Next(double x)
    {
        var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
        return y;
    }
}
