using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public interface IConfig
{
    string Type { get; }
}

public interface IConfigurable<T> where T : IConfig
{
    void Configure(T config);
}