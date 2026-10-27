namespace Plaat.Frontend;

public static class Redirects
{
    public static string GetAlbumHref(uint albumId)
    {
        return $"/albums/{albumId}";
    }

    public static string GetArtistHref(uint artistId)
    {
        return $"/artists/{artistId}";
    }

    public static string GetArtistAlbumsHref(uint artistId)
    {
        return $"/artists/{artistId}/albums";
    }
}
