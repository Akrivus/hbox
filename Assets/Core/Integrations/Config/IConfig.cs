public interface IConfig
{
    string Type { get; }
}

public interface IConfigurable<T> where T : IConfig
{
    void Configure(T config);
}