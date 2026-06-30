// WeatherState lives in the dependency-free Blockiverse.Core assembly (while keeping its original
// Blockiverse.WorldGen namespace) so that Blockiverse.UI can reference the enum without taking a
// direct Blockiverse.WorldGen assembly dependency (A2 decoupling). The enum is a pure value type
// with no dependencies, so hosting it in Core introduces no coupling.
namespace Blockiverse.WorldGen
{
    public enum WeatherState
    {
        Clear,
        PartlyCloudy,
        Overcast,
        LightRain,
        HeavyRain,
        Thunderstorm,
        LightSnow,
        HeavySnow,
        Blizzard,
        Fog,
    }
}
