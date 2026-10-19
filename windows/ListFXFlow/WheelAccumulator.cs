namespace ListFXFlow;

public sealed class WheelAccumulator
{
    private double remainder;
    public int Add(double logical) {
        double value=remainder+logical*3;
        int whole=(int)Math.Truncate(value); remainder=value-whole; return whole;
    }
    public void Reset() => remainder=0;
}
