﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaPortal.GUI.Library;
using MediaPortal.GUI.Video;
using MediaPortal.Video.Database;
using TraktPlugin.TraktAPI;
using TraktPlugin.TraktAPI.DataStructures;

namespace TraktPlugin.GUI
{
    #region Enums
    enum TrailerSiteMovies
    {
        IMDb,
        iTunes,
        YouTube
    }

    enum TrailerSiteShows
    {
        IMDb,
        YouTube
    }

    enum TraktGUIWindows
    {
        Main = 87258,
        Calendar = 87259,
        Friends = 87260,
        Recommendations = 87261,
        RecommendationsShows = 87262,
        RecommendationsMovies = 87263,
        Trending = 87264,
        TrendingShows = 87265,
        TrendingMovies = 87266,
        WatchedList = 87267,
        WatchedListShows = 87268,
        WatchedListEpisodes = 87269,
        WatchedListMovies = 87270,
        Settings = 87271,
        SettingsAccount = 87272,
        SettingsPlugins = 87273,
        SettingsGeneral = 87274,
        Lists = 87275,
        ListItems = 87276,
        RelatedMovies = 87277,
        RelatedShows = 87278,
        Shouts = 87280
    }

    enum TraktDashboardControls
    {
        ToggleTrendingCheckButton = 98298,
        DashboardAnimation = 98299,
        ActivityFacade = 98300,
        TrendingShowsFacade = 98301,
        TrendingMoviesFacade = 98302
    }

    enum ExternalPluginWindows
    {
        OnlineVideos = 4755,
        VideoInfo = 2003,
        MovingPictures = 96742,
        TVSeries = 9811,
        MyFilms = 7986,
        MyAnime = 6001,
        MpNZB = 3847,
        MPEISettings = 803,
        MyTorrents = 5678
    }

    enum ExternalPluginControls
    {
        WatchList = 97258,
        Rate = 97259,
        Shouts = 97260,
        CustomList = 97261,
        RelatedItems = 97262,
        TraktMenu = 97270
    }

    enum TraktMenuItems
    {
        AddToWatchList,
        AddToCustomList,
        Rate,
        Shouts,
        Related,
        Calendar,
        Recommendations,
        Trending,
        WatchList,
        Lists
    }
    #endregion

    public class GUICommon
    {
        #region Check Login
        public static bool CheckLogin()
        {
            return CheckLogin(true);
        }

        /// <summary>
        /// Checks if user is logged in, if not the user is presented with
        /// a choice to jump to Account settings and signup/login.
        /// </summary>
        public static bool CheckLogin(bool showPreviousWindow)
        {
            if (TraktSettings.AccountStatus != TraktAPI.ConnectionState.Connected)
            {
                if (GUIUtils.ShowYesNoDialog(Translation.Login, Translation.NotLoggedIn, true))
                {
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.SettingsAccount);
                    return false;
                }
                if (showPreviousWindow) GUIWindowManager.ShowPreviousWindow();
                return false;
            }
            return true;
        }
        #endregion

        #region Play Movie
        public static void CheckAndPlayMovie(bool jumpTo, TraktMovie movie)
        {
            if (movie == null) return;

            string title = movie.Title;
            string imdbid = movie.Imdb;
            string trailer = movie.Trailer;
            int year = Convert.ToInt32(movie.Year);

            CheckAndPlayMovie(jumpTo, title, year, imdbid, trailer);
        }

        /// <summary>
        /// Checks if a selected movie exists locally and plays movie or
        /// jumps to corresponding plugin details view
        /// </summary>
        /// <param name="jumpTo">false if movie should be played directly</param>
        public static void CheckAndPlayMovie(bool jumpTo, string title, int year, string imdbid)
        {
            CheckAndPlayMovie(jumpTo, title, year, imdbid, null);
        }
        public static void CheckAndPlayMovie(bool jumpTo, string title, int year, string imdbid, string trailer)
        {
            TraktLogger.Info("Attempting to play movie: {0} ({1}) [{2}]", title, year, imdbid);
            bool handled = false;

            if (TraktHelper.IsMovingPicturesAvailableAndEnabled)
            {
                TraktLogger.Info("Checking if any movie to watch in MovingPictures");
                int? movieid = null;

                // Find Movie ID in MovingPictures
                // Movie List is now cached internally in MovingPictures so it will be fast
                bool movieExists = TraktHandlers.MovingPictures.FindMovieID(title, year, imdbid, ref movieid);

                if (movieExists)
                {
                    TraktLogger.Info("Found movie in MovingPictures with movieId '{0}'", movieid.ToString());
                    if (jumpTo)
                    {
                        string loadingParameter = string.Format("movieid:{0}", movieid);
                        // Open MovingPictures Details view so user can play movie
                        GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MovingPictures, loadingParameter);
                    }
                    else
                    {
                        TraktHandlers.MovingPictures.PlayMovie(movieid);
                    }
                    handled = true;
                }
            }

            // check if its in My Videos database
            if (TraktSettings.MyVideos >= 0 && handled == false)
            {
                TraktLogger.Info("Checking if any movie to watch in My Videos");
                IMDBMovie movie = null;
                if (TraktHandlers.MyVideos.FindMovieID(title, year, imdbid, ref movie))
                {
                    // Open My Videos Video Info view so user can play movie
                    if (jumpTo)
                    {
                        GUIVideoInfo videoInfo = (GUIVideoInfo)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_VIDEO_INFO);
                        videoInfo.Movie = movie;
                        GUIWindowManager.ActivateWindow((int)GUIWindow.Window.WINDOW_VIDEO_INFO);
                    }
                    else
                    {
                        GUIVideoFiles.PlayMovie(movie.ID, false);
                    }
                    handled = true;
                }
            }

            // check if its in My Films database
            if (TraktHelper.IsMyFilmsAvailableAndEnabled && handled == false)
            {
                TraktLogger.Info("Checking if any movie to watch in My Films");
                int? movieid = null;
                string config = null;
                if (TraktHandlers.MyFilmsHandler.FindMovie(title, year, imdbid, ref movieid, ref config))
                {
                    // Open My Films Details view so user can play movie
                    if (jumpTo)
                    {
                        string loadingParameter = string.Format("config:{0}|movieid:{1}", config, movieid);
                        GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MyFilms, loadingParameter);
                    }
                    else
                    {
                        // TraktHandlers.MyFilms.PlayMovie(config, movieid); // ToDo: Add Player Class to MyFilms
                        string loadingParameter = string.Format("config:{0}|movieid:{1}|play:{2}", config, movieid, "true");
                        GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.MyFilms, loadingParameter);
                    }
                    handled = true;
                }
            }

            if (TraktHelper.IsOnlineVideosAvailableAndEnabled && handled == false)
            {
                if (!string.IsNullOrEmpty(trailer))
                {
                    TraktLogger.Info("No movies found! Attempting to play trailer.");
                    TraktHandlers.OnlineVideos.Play(trailer);
                    return;
                }

                TraktLogger.Info("No movies found! Attempting Trailer lookup in IMDb Trailers.");
                string loadingParameter = string.Format("site:IMDb Movie Trailers|search:{0}|return:Locked", imdbid);
                GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.OnlineVideos, loadingParameter);
                handled = true;
            }
        }
        #endregion

        #region PlayEpisode
        public static void CheckAndPlayEpisode(TraktShow show, TraktEpisode episode)
        {
            if (show == null || episode == null) return;
            CheckAndPlayEpisode(Convert.ToInt32(show.Tvdb), string.IsNullOrEmpty(show.Imdb) ? show.Title : show.Imdb, episode.Season, episode.Number);
        }

        /// <summary>
        /// Checks if a selected episode exists locally and plays episode
        /// </summary>
        /// <param name="seriesid">the series tvdb id of episode</param>
        /// <param name="imdbid">the series imdb id of episode</param>
        /// <param name="seasonidx">the season index of episode</param>
        /// <param name="episodeidx">the episode index of episode</param>
        public static void CheckAndPlayEpisode(int seriesid, string imdbid, int seasonidx, int episodeidx)
        {
            bool handled = false;

            // check if plugin is installed and enabled
            if (TraktHelper.IsMPTVSeriesAvailableAndEnabled)
            {
                // Play episode if it exists
                handled = TraktHandlers.TVSeries.PlayEpisode(seriesid, seasonidx, episodeidx);
            }

            if (TraktHelper.IsMyAnimeAvailableAndEnabled && handled == false)
            {
                handled = TraktHandlers.MyAnime.PlayEpisode(seriesid, seasonidx, episodeidx);
            }

            if (TraktHelper.IsOnlineVideosAvailableAndEnabled && handled == false)
            {
                TraktLogger.Info("No episodes found! Attempting Trailer lookup in IMDb Trailers.");
                string loadingParameter = string.Format("site:IMDb Movie Trailers|search:{0}|return:Locked", imdbid);
                GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.OnlineVideos, loadingParameter);
                handled = true;
            }
        }

        public static void CheckAndPlayFirstUnwatched(TraktShow show)
        {
            CheckAndPlayFirstUnwatched(show, false);
        }
        public static void CheckAndPlayFirstUnwatched(TraktShow show, bool jumpTo)
        {
            if (show == null) return;
            CheckAndPlayFirstUnwatched(Convert.ToInt32(show.Tvdb), string.IsNullOrEmpty(show.Imdb) ? show.Title : show.Imdb, jumpTo);
        }
        
        /// <summary>
        /// Checks if a selected show exists locally and plays first unwatched episode
        /// </summary>
        /// <param name="seriesid">the series tvdb id of show</param>
        /// <param name="imdbid">the series imdb id of show</param>
        public static void CheckAndPlayFirstUnwatched(int seriesid, string imdbid)
        {
            CheckAndPlayFirstUnwatched(seriesid, imdbid, false);
        }
        public static void CheckAndPlayFirstUnwatched(int seriesid, string imdbid, bool jumpTo)
        {
            TraktLogger.Info("Attempting to play TVDb: {0}, IMDb: {1}", seriesid.ToString(), imdbid);
            bool handled = false;

            // check if plugin is installed and enabled
            if (TraktHelper.IsMPTVSeriesAvailableAndEnabled)
            {
                if (jumpTo)
                {
                    TraktLogger.Info("Looking for series in MP-TVSeries database");
                    if (TraktHandlers.TVSeries.SeriesExists(seriesid))
                    {
                        string loadingParameter = string.Format("seriesid:{0}", seriesid);
                        GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.TVSeries, loadingParameter);
                        handled = true;
                    }
                }
                else
                {
                    // Play episode if it exists
                    TraktLogger.Info("Checking if any episodes to watch in MP-TVSeries");
                    handled = TraktHandlers.TVSeries.PlayFirstUnwatchedEpisode(seriesid);

                }
            }

            if (TraktHelper.IsMyAnimeAvailableAndEnabled && handled == false)
            {
                TraktLogger.Info("Checking if any episodes to watch in My Anime");
                handled = TraktHandlers.MyAnime.PlayFirstUnwatchedEpisode(seriesid);
            }

            if (TraktHelper.IsOnlineVideosAvailableAndEnabled && handled == false)
            {
                TraktLogger.Info("No episodes found! Attempting Trailer lookup in IMDb Trailers.");
                string loadingParameter = string.Format("site:IMDb Movie Trailers|search:{0}|return:Locked", imdbid);
                GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.OnlineVideos, loadingParameter);
                handled = true;
            }
        }
        #endregion

        #region Rate Movie

        internal static bool RateMovie(TraktMovie movie)
        {
            TraktRateMovie rateObject = new TraktRateMovie
            {
                IMDBID = movie.Imdb,
                TMDBID = movie.Tmdb,
                Title = movie.Title,
                Year = movie.Year,
                Rating = movie.RatingAdvanced.ToString(),
                UserName = TraktSettings.Username,
                Password = TraktSettings.Password
            };

            int prevRating = movie.RatingAdvanced;
            int newRating = int.Parse(GUIUtils.ShowRateDialog<TraktRateMovie>(rateObject));
            if (newRating == -1) return false;

            // If previous rating not equal to current rating then 
            // update skin properties to reflect changes
            // This is not really needed but saves waiting for response
            // from server to calculate fields...we can do it ourselves

            if (prevRating != newRating)
            {
                movie.RatingAdvanced = newRating;

                // if not rated previously bump up the votes
                if (prevRating == 0)
                {
                    movie.Ratings.Votes++;
                    if (movie.RatingAdvanced > 5)
                    {
                        movie.Rating = "love";
                        movie.Ratings.LovedCount++;
                    }
                    else
                    {
                        movie.Rating = "hate";
                        movie.Ratings.HatedCount++;
                    }
                }

                if (prevRating != 0 && prevRating > 5 && newRating <= 5)
                {
                    movie.Rating = "hate";
                    movie.Ratings.LovedCount--;
                    movie.Ratings.HatedCount++;
                }

                if (prevRating != 0 && prevRating <= 5 && newRating > 5)
                {
                    movie.Rating = "love";
                    movie.Ratings.LovedCount++;
                    movie.Ratings.HatedCount--;
                }

                if (newRating == 0)
                {
                    if (prevRating <= 5) movie.Ratings.HatedCount++;
                    movie.Ratings.Votes--;
                    movie.Rating = "false";
                }

                // Could be in-accurate, best guess
                if (prevRating == 0)
                {
                    movie.Ratings.Percentage = (int)Math.Round(((movie.Ratings.Percentage * (movie.Ratings.Votes - 1)) + (10 * newRating)) / (float)movie.Ratings.Votes);
                }
                else
                {
                    movie.Ratings.Percentage = (int)Math.Round(((movie.Ratings.Percentage * (movie.Ratings.Votes)) + (10 * newRating) - (10 * prevRating)) / (float)movie.Ratings.Votes);
                }

                return true;
            }

            return false;
        }

        #endregion

        #region Rate Show

        internal static bool RateShow(TraktShow show)
        {
            TraktRateSeries rateObject = new TraktRateSeries
            {
                SeriesID = show.Tvdb,
                Title = show.Title,
                Year = show.Year.ToString(),
                Rating = show.RatingAdvanced.ToString(),
                UserName = TraktSettings.Username,
                Password = TraktSettings.Password
            };

            int prevRating = show.RatingAdvanced;
            int newRating = int.Parse(GUIUtils.ShowRateDialog<TraktRateSeries>(rateObject));
            if (newRating == -1) return false;

            // If previous rating not equal to current rating then 
            // update skin properties to reflect changes
            // This is not really needed but saves waiting for response
            // from server to calculate fields...we can do it ourselves

            if (prevRating != newRating)
            {
                show.RatingAdvanced = newRating;

                // if not rated previously bump up the votes
                if (prevRating == 0)
                {
                    show.Ratings.Votes++;
                    if (show.RatingAdvanced > 5)
                    {
                        show.Rating = "love";
                        show.Ratings.LovedCount++;
                    }
                    else
                    {
                        show.Rating = "hate";
                        show.Ratings.HatedCount++;
                    }
                }

                if (prevRating != 0 && prevRating > 5 && newRating <= 5)
                {
                    show.Rating = "hate";
                    show.Ratings.LovedCount--;
                    show.Ratings.HatedCount++;
                }

                if (prevRating != 0 && prevRating <= 5 && newRating > 5)
                {
                    show.Rating = "love";
                    show.Ratings.LovedCount++;
                    show.Ratings.HatedCount--;
                }

                if (newRating == 0)
                {
                    if (prevRating <= 5) show.Ratings.HatedCount++;
                    show.Ratings.Votes--;
                    show.Rating = "false";
                }

                // Could be in-accurate, best guess
                if (prevRating == 0)
                {
                    show.Ratings.Percentage = (int)Math.Round(((show.Ratings.Percentage * (show.Ratings.Votes - 1)) + (10 * newRating)) / (float)show.Ratings.Votes);
                }
                else
                {
                    show.Ratings.Percentage = (int)Math.Round(((show.Ratings.Percentage * (show.Ratings.Votes)) + (10 * newRating) - (10 * prevRating)) / (float)show.Ratings.Votes);
                }

                return true;
            }

            return false;
        }

        #endregion

        #region Rate Episode

        internal static bool RateEpisode(TraktShow show, TraktEpisode episode)
        {
            TraktRateEpisode rateObject = new TraktRateEpisode
            {
                SeriesID = show.Tvdb,
                Title = show.Title,
                Year = show.Year.ToString(),
                Episode = episode.Number.ToString(),
                Season = episode.Season.ToString(),
                Rating = episode.RatingAdvanced.ToString(),
                UserName = TraktSettings.Username,
                Password = TraktSettings.Password
            };

            int prevRating = episode.RatingAdvanced;
            int newRating = int.Parse(GUIUtils.ShowRateDialog<TraktRateEpisode>(rateObject));
            if (newRating == -1) return false;

            // If previous rating not equal to current rating then 
            // update skin properties to reflect changes
            // This is not really needed but saves waiting for response
            // from server to calculate fields...we can do it ourselves

            if (prevRating != newRating)
            {
                episode.RatingAdvanced = newRating;

                // if not rated previously bump up the votes
                if (prevRating == 0)
                {
                    episode.Ratings.Votes++;
                    if (episode.RatingAdvanced > 5)
                    {
                        episode.Rating = "love";
                        episode.Ratings.LovedCount++;
                    }
                    else
                    {
                        episode.Rating = "hate";
                        episode.Ratings.HatedCount++;
                    }
                }

                if (prevRating != 0 && prevRating > 5 && newRating <= 5)
                {
                    episode.Rating = "hate";
                    episode.Ratings.LovedCount--;
                    episode.Ratings.HatedCount++;
                }

                if (prevRating != 0 && prevRating <= 5 && newRating > 5)
                {
                    episode.Rating = "love";
                    episode.Ratings.LovedCount++;
                    episode.Ratings.HatedCount--;
                }

                if (newRating == 0)
                {
                    if (prevRating <= 5) show.Ratings.HatedCount++;
                    episode.Ratings.Votes--;
                    episode.Rating = "false";
                }

                // Could be in-accurate, best guess
                if (prevRating == 0)
                {
                    episode.Ratings.Percentage = (int)Math.Round(((show.Ratings.Percentage * (show.Ratings.Votes - 1)) + (10 * newRating)) / (float)show.Ratings.Votes);
                }
                else
                {
                    episode.Ratings.Percentage = (int)Math.Round(((show.Ratings.Percentage * (show.Ratings.Votes)) + (10 * newRating) - (10 * prevRating)) / (float)show.Ratings.Votes);
                }

                return true;
            }

            return false;
        }

        #endregion

        #region Common Skin Properties

        internal static void SetProperty(string property, string value)
        {
            string propertyValue = string.IsNullOrEmpty(value) ? "N/A" : value;
            GUIUtils.SetProperty(property, propertyValue);
        }

        internal static void ClearUserProperties()
        {
            GUIUtils.SetProperty("#Trakt.User.About", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.Age", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.Avatar", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.AvatarFileName", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.FullName", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.Gender", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.JoinDate", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.ApprovedDate", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.Location", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.Protected", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.Url", string.Empty);
            GUIUtils.SetProperty("#Trakt.User.Username", string.Empty);
        }

        internal static void SetUserProperties(TraktUserProfile user)
        {
            SetProperty("#Trakt.User.About", user.About);
            SetProperty("#Trakt.User.Age", user.Age);
            SetProperty("#Trakt.User.Avatar", user.Avatar);
            SetProperty("#Trakt.User.AvatarFileName", user.AvatarFilename);
            SetProperty("#Trakt.User.FullName", user.FullName);
            SetProperty("#Trakt.User.Gender", user.Gender);
            SetProperty("#Trakt.User.JoinDate", user.JoinDate.FromEpoch().ToLongDateString());
            SetProperty("#Trakt.User.ApprovedDate", user.ApprovedDate == 0 ? "N/A" : user.ApprovedDate.FromEpoch().ToLongDateString());
            SetProperty("#Trakt.User.Location", user.Location);
            SetProperty("#Trakt.User.Protected", user.Protected);
            SetProperty("#Trakt.User.Url", user.Url);
            SetProperty("#Trakt.User.Username", user.Username);
        }

        internal static void ClearMovieProperties()
        {
            GUIUtils.SetProperty("#Trakt.Movie.Imdb", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Certification", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Overview", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Released", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Runtime", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Tagline", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Title", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Tmdb", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Trailer", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Url", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Year", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Genres", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.PosterImageFilename", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.FanartImageFilename", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.InCollection", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.InWatchList", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Plays", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Watched", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Rating", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.RatingAdvanced", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.Icon", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.HatedCount", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.LovedCount", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.Percentage", string.Empty);
            GUIUtils.SetProperty("#Trakt.Movie.Ratings.Votes", string.Empty);
        }

        internal static void SetMovieProperties(TraktMovie movie)
        {
            if (movie == null) return;

            SetProperty("#Trakt.Movie.Imdb", movie.Imdb);
            SetProperty("#Trakt.Movie.Certification", movie.Certification);
            SetProperty("#Trakt.Movie.Overview", string.IsNullOrEmpty(movie.Overview) ? Translation.NoMovieSummary : movie.Overview);
            SetProperty("#Trakt.Movie.Released", movie.Released.FromEpoch().ToShortDateString());
            SetProperty("#Trakt.Movie.Runtime", movie.Runtime.ToString());
            SetProperty("#Trakt.Movie.Tagline", movie.Tagline);
            SetProperty("#Trakt.Movie.Title", movie.Title);
            SetProperty("#Trakt.Movie.Tmdb", movie.Tmdb);
            SetProperty("#Trakt.Movie.Trailer", movie.Trailer);
            SetProperty("#Trakt.Movie.Url", movie.Url);
            SetProperty("#Trakt.Movie.Year", movie.Year);
            SetProperty("#Trakt.Movie.Genres", string.Join(", ", movie.Genres.ToArray()));
            SetProperty("#Trakt.Movie.PosterImageFilename", movie.Images.PosterImageFilename);
            SetProperty("#Trakt.Movie.FanartImageFilename", movie.Images.FanartImageFilename);
            SetProperty("#Trakt.Movie.InCollection", movie.InCollection.ToString());
            SetProperty("#Trakt.Movie.InWatchList", movie.InWatchList.ToString());
            SetProperty("#Trakt.Movie.Plays", movie.Plays.ToString());
            SetProperty("#Trakt.Movie.Watched", movie.Watched.ToString());
            SetProperty("#Trakt.Movie.Rating", movie.Rating);
            SetProperty("#Trakt.Movie.RatingAdvanced", movie.RatingAdvanced.ToString());
            SetProperty("#Trakt.Movie.Ratings.Icon", (movie.Ratings.LovedCount > movie.Ratings.HatedCount) ? "love" : "hate");
            SetProperty("#Trakt.Movie.Ratings.HatedCount", movie.Ratings.HatedCount.ToString());
            SetProperty("#Trakt.Movie.Ratings.LovedCount", movie.Ratings.LovedCount.ToString());
            SetProperty("#Trakt.Movie.Ratings.Percentage", movie.Ratings.Percentage.ToString());
            SetProperty("#Trakt.Movie.Ratings.Votes", movie.Ratings.Votes.ToString());
        }

        internal static void ClearShowProperties()
        {
            GUIUtils.SetProperty("#Trakt.Show.Imdb", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Tvdb", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.TvRage", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Title", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Url", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.AirDay", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.AirTime", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Certification", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Country", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.FirstAired", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Network", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Overview", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Runtime", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Year", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Genres", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.InWatchList", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Rating", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.RatingAdvanced", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Ratings.Icon", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Ratings.HatedCount", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Ratings.LovedCount", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Ratings.Percentage", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.Ratings.Votes", string.Empty);
            GUIUtils.SetProperty("#Trakt.Show.FanartImageFilename", string.Empty);
        }

        internal static void SetShowProperties(TraktShow show)
        {
            if (show == null) return;

            SetProperty("#Trakt.Show.Imdb", show.Imdb);
            SetProperty("#Trakt.Show.Tvdb", show.Tvdb);
            SetProperty("#Trakt.Show.TvRage", show.TvRage);
            SetProperty("#Trakt.Show.Title", show.Title);
            SetProperty("#Trakt.Show.Url", show.Url);
            SetProperty("#Trakt.Show.AirDay", show.AirDay);
            SetProperty("#Trakt.Show.AirTime", show.AirTime);
            SetProperty("#Trakt.Show.Certification", show.Certification);
            SetProperty("#Trakt.Show.Country", show.Country);
            SetProperty("#Trakt.Show.FirstAired", show.FirstAired.FromEpoch().ToShortDateString());
            SetProperty("#Trakt.Show.Network", show.Network);
            SetProperty("#Trakt.Show.Overview", string.IsNullOrEmpty(show.Overview) ? Translation.NoShowSummary : show.Overview);
            SetProperty("#Trakt.Show.Runtime", show.Runtime.ToString());
            SetProperty("#Trakt.Show.Year", show.Year.ToString());
            SetProperty("#Trakt.Show.Genres", string.Join(", ", show.Genres.ToArray()));
            SetProperty("#Trakt.Show.InWatchList", show.InWatchList.ToString());
            SetProperty("#Trakt.Show.Rating", show.Rating);
            SetProperty("#Trakt.Show.RatingAdvanced", show.RatingAdvanced.ToString());
            SetProperty("#Trakt.Show.Ratings.Icon", (show.Ratings.LovedCount > show.Ratings.HatedCount) ? "love" : "hate");
            SetProperty("#Trakt.Show.Ratings.HatedCount", show.Ratings.HatedCount.ToString());
            SetProperty("#Trakt.Show.Ratings.LovedCount", show.Ratings.LovedCount.ToString());
            SetProperty("#Trakt.Show.Ratings.Percentage", show.Ratings.Percentage.ToString());
            SetProperty("#Trakt.Show.Ratings.Votes", show.Ratings.Votes.ToString());
            SetProperty("#Trakt.Show.FanartImageFilename", show.Images.FanartImageFilename);
        }

        internal static void ClearEpisodeProperties()
        {
            GUIUtils.SetProperty("#Trakt.Episode.Number", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Season", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.FirstAired", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Title", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Url", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Overview", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Runtime", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.InWatchList", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.InCollection", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Plays", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Watched", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Rating", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.RatingAdvanced", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Ratings.Icon", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Ratings.HatedCount", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Ratings.LovedCount", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Ratings.Percentage", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.Ratings.Votes", string.Empty);
            GUIUtils.SetProperty("#Trakt.Episode.EpisodeImageFilename", string.Empty);
        }

        internal static void SetEpisodeProperties(TraktEpisode episode)
        {
            if (episode == null) return;

            SetProperty("#Trakt.Episode.Number", episode.Number.ToString());
            SetProperty("#Trakt.Episode.Season", episode.Season.ToString());
            SetProperty("#Trakt.Episode.FirstAired", episode.FirstAired.FromEpoch().ToShortDateString());
            SetProperty("#Trakt.Episode.Title", string.IsNullOrEmpty(episode.Title) ? string.Format("{0} {1}", Translation.Episode, episode.Number.ToString()) : episode.Title);
            SetProperty("#Trakt.Episode.Url", episode.Url);
            SetProperty("#Trakt.Episode.Overview", string.IsNullOrEmpty(episode.Overview) ? Translation.NoEpisodeSummary : episode.Overview);
            SetProperty("#Trakt.Episode.Runtime", episode.Runtime.ToString());
            SetProperty("#Trakt.Episode.InWatchList", episode.InWatchList.ToString());
            SetProperty("#Trakt.Episode.InCollection", episode.InCollection.ToString());
            SetProperty("#Trakt.Episode.Plays", episode.Plays.ToString());
            SetProperty("#Trakt.Episode.Watched", episode.Watched.ToString());
            SetProperty("#Trakt.Episode.Rating", episode.Rating);
            SetProperty("#Trakt.Episode.RatingAdvanced", episode.RatingAdvanced.ToString());
            SetProperty("#Trakt.Episode.Ratings.Icon", (episode.Ratings.LovedCount > episode.Ratings.HatedCount) ? "love" : "hate");
            SetProperty("#Trakt.Episode.Ratings.HatedCount", episode.Ratings.HatedCount.ToString());
            SetProperty("#Trakt.Episode.Ratings.LovedCount", episode.Ratings.LovedCount.ToString());
            SetProperty("#Trakt.Episode.Ratings.Percentage", episode.Ratings.Percentage.ToString());
            SetProperty("#Trakt.Episode.Ratings.Votes", episode.Ratings.Votes.ToString());
            SetProperty("#Trakt.Episode.EpisodeImageFilename", episode.Images.EpisodeImageFilename);
        }

        internal static void ClearSeasonProperties()
        {
            GUIUtils.SetProperty("#Trakt.Season.Number", string.Empty);
        }

        #endregion

        #region GUI Context Menus

        #region Movie Trailers

        public static void ShowMovieTrailersMenu(TraktMovie movie)
        {
            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            dlg.Reset();
            dlg.SetHeading(Translation.Trailer);

            if (!string.IsNullOrEmpty(movie.Trailer))
            {
                // trailer can be played without searching
                GUIListItem pItem = new GUIListItem(Translation.PlayTrailer);
                dlg.Add(pItem);
            }

            foreach (TrailerSiteMovies site in Enum.GetValues(typeof(TrailerSiteMovies)))
            {
                string menuItem = Enum.GetName(typeof(TrailerSiteMovies), site);
                GUIListItem pItem = new GUIListItem(menuItem);
                dlg.Add(pItem);
            }
            
            dlg.DoModal(GUIWindowManager.ActiveWindow);

            if (dlg.SelectedLabel >= 0)
            {
                string siteUtil = string.Empty;
                string searchParam = string.Empty;

                switch (dlg.SelectedLabelText)
                {
                    case ("IMDb"):
                        siteUtil = "IMDb Movie Trailers";
                        if (!string.IsNullOrEmpty(movie.Imdb))
                            // Exact search
                            searchParam = movie.Imdb;
                        else
                            searchParam = movie.Title;
                        break;

                    case ("iTunes"):
                        siteUtil = "iTunes Movie Trailers";
                        searchParam = movie.Title;
                        break;

                    case ("YouTube"):
                        siteUtil = "YouTube";
                        searchParam = movie.Title;
                        break;

                    default:
                        if (TraktHelper.IsOnlineVideosAvailableAndEnabled)
                        {
                            TraktHandlers.OnlineVideos.Play(movie.Trailer);
                        }
                        return;
                }

                string loadingParam = string.Format("site:{0}|search:{1}|return:Locked", siteUtil, searchParam);
                
                // Launch OnlineVideos Trailer search
                GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.OnlineVideos, loadingParam);
            }
        }

        #endregion

        #region TV Show Trailers
        public static void ShowTVShowTrailersMenu(TraktShow show)
        {
            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            dlg.Reset();
            dlg.SetHeading(Translation.Trailer);

            foreach (TrailerSiteShows site in Enum.GetValues(typeof(TrailerSiteShows)))
            {
                string menuItem = Enum.GetName(typeof(TrailerSiteShows), site);
                GUIListItem pItem = new GUIListItem(menuItem);
                dlg.Add(pItem);
            }

            dlg.DoModal(GUIWindowManager.ActiveWindow);

            if (dlg.SelectedLabel >= 0)
            {
                string siteUtil = string.Empty;
                string searchParam = string.Empty;

                switch (dlg.SelectedLabelText)
                {
                    case ("IMDb"):
                        siteUtil = "IMDb Movie Trailers";
                        if (!string.IsNullOrEmpty(show.Imdb))
                            // Exact search
                            searchParam = show.Imdb;
                        else
                            searchParam = show.Title;
                        break;

                    case ("YouTube"):
                        siteUtil = "YouTube";
                        searchParam = show.Title;
                        break;
                }

                string loadingParam = string.Format("site:{0}|search:{1}|return:Locked", siteUtil, searchParam);

                // Launch OnlineVideos Trailer search
                GUIWindowManager.ActivateWindow((int)ExternalPluginWindows.OnlineVideos, loadingParam);
            }
        }
        #endregion

        #region Trakt External Menu

        #region Movies
        public static void ShowTraktExtMovieMenu(string title, string year, string imdbid, string fanart)
        {
            ShowTraktExtMovieMenu(title, year, imdbid, fanart, false);
        }
        public static void ShowTraktExtMovieMenu(string title, string year, string imdbid, string fanart, bool showAll)
        {
            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            GUIListItem pItem = new GUIListItem(Translation.Rate);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.Rate;

            pItem = new GUIListItem(Translation.Shouts);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.Shouts;

            pItem = new GUIListItem(Translation.RelatedMovies);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.Related;

            pItem = new GUIListItem(Translation.AddToWatchList);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.AddToWatchList;

            pItem = new GUIListItem(Translation.AddToList);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.AddToCustomList;

            // also show non-context sensitive items related to movies
            if (showAll)
            {
                pItem = new GUIListItem(Translation.Recommendations);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.Recommendations;

                pItem = new GUIListItem(Translation.Trending);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.Trending;

                pItem = new GUIListItem(Translation.WatchList);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.WatchList;

                pItem = new GUIListItem(Translation.Lists);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.Lists;
            }

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)TraktMenuItems.Rate):
                    TraktLogger.Info("Rating movie '{0} ({1}) [{2}]'", title, year, imdbid);
                    GUIUtils.ShowRateDialog<TraktRateMovie>(TraktHandlers.BasicHandler.CreateMovieRateData(title, year, imdbid));
                    break;

                case ((int)TraktMenuItems.Shouts):
                    TraktLogger.Info("Searching Shouts for movie '{0} ({1}) [{2}]'", title, year, imdbid);
                    TraktHelper.ShowMovieShouts(imdbid, title, year, fanart);
                    break;

                case ((int)TraktMenuItems.Related):
                    TraktLogger.Info("Show Related Movies for '{0} ({1}) [{2}]'", title, year, imdbid);
                    TraktHelper.ShowRelatedMovies(imdbid, title, year);
                    break;

                case ((int)TraktMenuItems.AddToWatchList):
                    TraktLogger.Info("Adding movie '{0} ({1}) [{2}]' to Watch List", title, year, imdbid);
                    TraktHelper.AddMovieToWatchList(title, year, imdbid, true);
                    break;

                case ((int)TraktMenuItems.AddToCustomList):
                    TraktLogger.Info("Adding movie '{0} ({1}) [{2}]' to Custom List", title, year, imdbid);
                    TraktHelper.AddRemoveMovieInUserList(title, year, imdbid, false);
                    break;

                case ((int)TraktMenuItems.Recommendations):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.RecommendationsMovies);
                    break;

                case ((int)TraktMenuItems.Trending):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.TrendingMovies);
                    break;

                case ((int)TraktMenuItems.WatchList):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.WatchedListMovies);
                    break;

                case ((int)TraktMenuItems.Lists):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Lists);
                    break;
            }
        }

        #endregion

        #region Shows
        public static void ShowTraktExtTVShowMenu(string title, string year, string tvdbid, string fanart)
        {
            ShowTraktExtTVShowMenu(title, year, tvdbid, fanart, false);
        }
        public static void ShowTraktExtTVShowMenu(string title, string year, string tvdbid, string fanart, bool showAll)
        {
            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            GUIListItem pItem = new GUIListItem(Translation.Rate);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.Rate;

            pItem = new GUIListItem(Translation.Shouts);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.Shouts;

            pItem = new GUIListItem(Translation.RelatedShows);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.Related;

            pItem = new GUIListItem(Translation.AddToWatchList);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.AddToWatchList;

            pItem = new GUIListItem(Translation.AddToList);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.AddToCustomList;

            // also show non-context sensitive items related to shows
            if (showAll)
            {
                pItem = new GUIListItem(Translation.Calendar);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.Calendar;

                pItem = new GUIListItem(Translation.Recommendations);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.Recommendations;

                pItem = new GUIListItem(Translation.Trending);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.Trending;

                pItem = new GUIListItem(Translation.WatchList);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.WatchList;

                pItem = new GUIListItem(Translation.Lists);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.Lists;
            }

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)TraktMenuItems.Rate):
                    TraktLogger.Info("Rating show '{0}'", title);
                    GUIUtils.ShowRateDialog<TraktRateSeries>(TraktHandlers.BasicHandler.CreateShowRateData(title, tvdbid));
                    break;

                case ((int)TraktMenuItems.Shouts):
                    TraktLogger.Info("Searching Shouts for show '{0}'", title);
                    TraktHelper.ShowTVShowShouts(tvdbid, title, fanart);
                    break;

                case ((int)TraktMenuItems.Related):
                    TraktLogger.Info("Show Related Shows for '{0}'", title);
                    TraktHelper.ShowRelatedShows(tvdbid, title);
                    break;

                case ((int)TraktMenuItems.AddToWatchList):
                    TraktLogger.Info("Adding show '{0}' to Watch List", title);
                    TraktHelper.AddShowToWatchList(title, null, tvdbid);
                    break;

                case ((int)TraktMenuItems.AddToCustomList):
                    TraktLogger.Info("Adding show '{0}' to Custom List", title);
                    TraktHelper.AddRemoveShowInUserList(title, null, tvdbid, false);
                    break;

                case ((int)TraktMenuItems.Calendar):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Calendar);
                    break;

                case ((int)TraktMenuItems.Recommendations):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.RecommendationsShows);
                    break;

                case ((int)TraktMenuItems.Trending):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.TrendingShows);
                    break;

                case ((int)TraktMenuItems.WatchList):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.WatchedListShows);
                    break;

                case ((int)TraktMenuItems.Lists):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Lists);
                    break;
            }
        }
        #endregion

        #region Episodes
        public static void ShowTraktExtEpisodeMenu(string title, string year, string season, string episode, string tvdbid, string fanart)
        {
            ShowTraktExtEpisodeMenu(title, year, season, episode, tvdbid, fanart, false);
        }
        public static void ShowTraktExtEpisodeMenu(string title, string year, string season, string episode, string tvdbid, string fanart, bool showAll)
        {
            IDialogbox dlg = (IDialogbox)GUIWindowManager.GetWindow((int)GUIWindow.Window.WINDOW_DIALOG_MENU);
            dlg.Reset();
            dlg.SetHeading(GUIUtils.PluginName());

            GUIListItem pItem = new GUIListItem(Translation.Rate);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.Rate;

            pItem = new GUIListItem(Translation.Shouts);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.Shouts;

            pItem = new GUIListItem(Translation.AddToWatchList);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.AddToWatchList;

            pItem = new GUIListItem(Translation.AddToList);
            dlg.Add(pItem);
            pItem.ItemId = (int)TraktMenuItems.AddToCustomList;

            // also show non-context sensitive items related to episodes
            if (showAll)
            {
                pItem = new GUIListItem(Translation.Calendar);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.Calendar;

                pItem = new GUIListItem(Translation.WatchList);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.WatchList;

                pItem = new GUIListItem(Translation.Lists);
                dlg.Add(pItem);
                pItem.ItemId = (int)TraktMenuItems.Lists;
            }

            // Show Context Menu
            dlg.DoModal(GUIWindowManager.ActiveWindow);
            if (dlg.SelectedId < 0) return;

            switch (dlg.SelectedId)
            {
                case ((int)TraktMenuItems.Rate):
                    TraktLogger.Info("Rating episode '{0} - {1}x{2}'", title, season, episode);
                    GUIUtils.ShowRateDialog<TraktRateEpisode>(TraktHandlers.BasicHandler.CreateEpisodeRateData(title, tvdbid, season, episode));
                    break;

                case ((int)TraktMenuItems.Shouts):
                    TraktLogger.Info("Searching Shouts for episode '{0} - {1}x{2}'", title, season, episode);
                    TraktHelper.ShowEpisodeShouts(tvdbid, title, season, episode, fanart);
                    break;

                case ((int)TraktMenuItems.AddToWatchList):
                    TraktLogger.Info("Adding episode '{0} - {1}x{2}' to Watch List", title, season, episode);
                    TraktHelper.AddEpisodeToWatchList(title, year, tvdbid, season, episode);
                    break;

                case ((int)TraktMenuItems.AddToCustomList):
                    TraktLogger.Info("Adding episode '{0} - {1}x{2}' to Custom List", title, season, episode);
                    TraktHelper.AddRemoveEpisodeInUserList(title, year, season, episode, tvdbid, false);
                    break;

                case ((int)TraktMenuItems.Calendar):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Calendar);
                    break;

                case ((int)TraktMenuItems.WatchList):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.WatchedListEpisodes);
                    break;

                case ((int)TraktMenuItems.Lists):
                    GUIWindowManager.ActivateWindow((int)TraktGUIWindows.Lists);
                    break;
            }
        }
        #endregion

        #endregion

        #endregion

    }
}
