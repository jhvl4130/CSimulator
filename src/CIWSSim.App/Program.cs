using CIWSSim.Core;
using CIWSSim.Core.Geometry;
using CIWSSim.Models;

var engine = new Engine();
int id = 1;

engine.AddAirplane(id++, 0, 5.0, 0, 2.0, 90.0, 0.0, 0.0);
engine.AddAirplane(id++, 0, 7.0, 0, 1.2, 90.0, 0.0, 0.0);

var building = new Building
{
    Polygon = new List<XYPos>
    {
        new(10, 10), new(20, 10), new(20, -10), new(10, -10)
    },
    Bottom = 0,
    Top = 20
};
engine.AddAsset(id++, building);

engine.AddLauncher(id++, 10000, 2, 0.5,
    0.0, 1.3, 0.0, 100.0,
    100.0, 100.0, 0.0,
    90.0, 45.0, 3.0);

engine.AddLauncher(id++, 11000, 2, 0.5,
    0.0, 1.3, 0.0, 100.0,
    100.0, 100.0, 0.0,
    90.0, 45.0, 5.0);

engine.Start(100.0);
