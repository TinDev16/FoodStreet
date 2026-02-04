using FoodStreetMobile.Models;

namespace FoodStreetMobile.Services;

public sealed class PoiRepository
{
    public IReadOnlyList<Poi> GetPois()
    {
        return new List<Poi>
        {
            new()
            {
                Id = "banh-canh",
                Name = "Banh canh ghe Vinh Khanh",
                Latitude = 10.755622,
                Longitude = 106.709866,
                RadiusMeters = 45,
                Priority = 3,
                Narration = "Banh canh ghe Vinh Khanh - noi bat voi nuoc le dam da va ghe tuoi. Hay thu mon dac trung cua con pho nay."
            },
            new()
            {
                Id = "lau-hai-san",
                Name = "Lau hai san gia dinh",
                Latitude = 10.755242,
                Longitude = 106.709085,
                RadiusMeters = 40,
                Priority = 2,
                Narration = "Quan lau hai san am cung, phu hop di nhom. Nuoc lau thanh ngot, do hai san tuoi."
            },
            new()
            {
                Id = "oc-hoa",
                Name = "Oc Hoa",
                Latitude = 10.754980,
                Longitude = 106.709580,
                RadiusMeters = 40,
                Priority = 2,
                Narration = "Oc Hoa noi tieng voi thuc don oc phong phu, xu ly nhanh, gia de chiu."
            },
            new()
            {
                Id = "ha-cao",
                Name = "Ha cao chien",
                Latitude = 10.755430,
                Longitude = 106.710320,
                RadiusMeters = 35,
                Priority = 1,
                Narration = "Ha cao chien gion, nhan tom thit, an kem nuoc cham chua ngot."
            },
            new()
            {
                Id = "tra-sua",
                Name = "Tra sua mat ong",
                Latitude = 10.754680,
                Longitude = 106.710020,
                RadiusMeters = 35,
                Priority = 1,
                Narration = "Tra sua mat ong mat lanh de giai nhiet trong luc kham pha con pho am thuc."
            }
        };
    }
}
