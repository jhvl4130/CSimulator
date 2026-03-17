namespace CIWSSim.Core;

public enum ModelClass
{
    Platform = 0,
    Sensor = 1,
    C2 = 2,
    Weapon = 3,
    Asset = 4
}

public static class SimConstants
{
    public const double MovePeriod = 0.01;

    // Phase
    public const int PhaseWaitStart = 0;
    public const int PhaseRun = 1;

    public const int ClsNum = 5;

    // Model types - Platform
    public const int MtAirplane = 1;
    public const int MtMissile = 2;
    public const int MtUav = 3;
    public const int MtDrone = 4;
    public const int MtLauncher = 5;
    public const int MtRocket = 6;

    // Model types - Sensor
    public const int MtSRadar = 100;
    public const int MtTRadar = 101;
    public const int MtEots = 102;

    // Model types - C2
    public const int MtControl = 200;
    public const int MtFcs = 201;

    // Model types - Weapon
    public const int MtCiws = 300;
    public const int MtGun = 301;

    // Model types - Asset
    public const int MtAsset = 400;

    // Time
    public const double TContinue = -1.0;
    public const double TInfinite = 900_000_000_000.0;
    public const double TScale = 10_000_000.0;
}
