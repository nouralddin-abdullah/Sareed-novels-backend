namespace Application.Services;

public interface IViewTrackingService
{
    Task TrackNovelView(Guid novelId);
    Task<int> GetTodaysViews(Guid novelId);
}
