using CIWSSim.Core;
using CIWSSim.Core.Geometry;

namespace CIWSSim.Models;

public static class EngineExtensions
{
    public static void AddAirplane(this Engine engine, int id,
        double x, double y, double z, double speed,
        double azimuth, double elevation, double startT)
    {
        var model = new Airplane(id)
        {
            IniPos = new XYZPos(x, y, z),
            IniSpeed = speed,
            IniAzimuth = azimuth,
            IniElevation = elevation,
            StartT = startT
        };
        engine.RegisterModel(model);
    }

    public static void AddAsset(this Engine engine, int id, Building building)
    {
        var model = new Asset(id);
        model.SetBuilding(building);
        engine.RegisterAsset(model);
    }

    public static void AddLauncher(this Engine engine, int id,
        int startRktId, int rktNum, double period,
        double x, double y, double z, double speed,
        double gipX, double gipY, double gipZ,
        double azimuth, double elevation, double startT)
    {
        var model = new Launcher(id)
        {
            StartRktId = startRktId,
            RktNum = rktNum,
            FirePeriod = period,
            IniPos = new XYZPos(x, y, z),
            IniSpeed = speed,
            Gip = new XYZPos(gipX, gipY, gipZ),
            IniAzimuth = azimuth,
            IniElevation = elevation,
            StartT = startT
        };
        engine.RegisterModel(model);
    }
}
