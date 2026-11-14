namespace JacquardStudio;
public static class PreviewViewport
{
    public static double ZoomOffset(double imageCoordinate, double viewportCoordinate, double newZoom) => Math.Max(0, imageCoordinate * newZoom - viewportCoordinate);
    public static double PanOffset(double startOffset, double startPointer, double currentPointer) => Math.Max(0, startOffset + startPointer - currentPointer);
}
