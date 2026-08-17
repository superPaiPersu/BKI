namespace CityStateSim.Perception
{
    public interface IPerceivableDetailProvider
    {
        string BuildPerceptionDetail(PerceptionChannel channels);
    }
}
