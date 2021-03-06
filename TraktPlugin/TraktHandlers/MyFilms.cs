﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using TraktPlugin.GUI;
using TraktPlugin.TraktAPI;
using TraktPlugin.TraktAPI.DataStructures;
using MediaPortal.Player;
using MediaPortal.Configuration;
using System.Reflection;
using System.ComponentModel;
using MyFilmsPlugin.MyFilms;
using MyFilmsPlugin.MyFilms.MyFilmsGUI;
using System.Threading;

namespace TraktPlugin.TraktHandlers
{
    class MyFilmsHandler : ITraktHandler
    {
        #region Variables

        Timer TraktTimer;
        MFMovie CurrentMovie = null;
        bool SyncInProgress = false;

        #endregion

        #region Constructor

        public MyFilmsHandler(int priority)
        {
            // check if plugin exists otherwise plugin could accidently get added to list
            string pluginFilename = Path.Combine(Config.GetSubFolder(Config.Dir.Plugins, "Windows"), "MyFilms.dll");
            if (!File.Exists(pluginFilename))
                throw new FileNotFoundException("Plugin not found!");
            else
            {
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(pluginFilename);
                string version = fvi.ProductVersion;
                if (new Version(version) < new Version(6,0,0,2616))
                    throw new FileLoadException("Plugin does not meet minimum requirements!");
            }

            // Subscribe to Events
            TraktLogger.Debug("Adding Hooks to My Films");
            MyFilmsDetail.RateItem += new MyFilmsDetail.RatingEventDelegate(OnRateItem);
            MyFilmsDetail.WatchedItem += new MyFilmsDetail.WatchedEventDelegate(OnToggleWatched);
            MyFilmsDetail.MovieStarted += new MyFilmsDetail.MovieStartedEventDelegate(OnStartedMovie);
            MyFilmsDetail.MovieStopped += new MyFilmsDetail.MovieStoppedEventDelegate(OnStoppedMovie);
            MyFilmsDetail.MovieWatched += new MyFilmsDetail.MovieWatchedEventDelegate(OnWatchedMovie);
            MyFilms.ImportComplete += new MyFilms.ImportCompleteEventDelegate(OnImportComplete);

            Priority = priority;
        }

        #endregion

        #region ITraktHandler

        public string Name
        {
            get { return "My Films"; }
        }

        public int Priority { get; set; }
       
        public void SyncLibrary()
        {
            TraktLogger.Info("My Films Starting Sync");
            SyncInProgress = true;

            // get all movies
            ArrayList myvideos = new ArrayList();
            BaseMesFilms.GetMovies(ref myvideos);
            TraktLogger.Info("BaseMesFilms.GetMovies: returning " + myvideos.Count + " movies");

            List<MFMovie> MovieList = (from MFMovie movie in myvideos select movie).ToList();

            // Remove any blocked movies
            MovieList.RemoveAll(movie => TraktSettings.BlockedFolders.Any(f => movie.File.ToLowerInvariant().Contains(f.ToLowerInvariant())));
            MovieList.RemoveAll(movie => TraktSettings.BlockedFilenames.Contains(movie.File));

            #region Skipped Movies Check
            // Remove Skipped Movies from previous Sync
            if (TraktSettings.SkippedMovies != null)
            {
                // allow movies to re-sync again after 7-days in the case user has addressed issue ie. edited movie or added to themoviedb.org
                if (TraktSettings.SkippedMovies.LastSkippedSync.FromEpoch() > DateTime.UtcNow.Subtract(new TimeSpan(7, 0, 0, 0)))
                {
                    if (TraktSettings.SkippedMovies.Movies != null && TraktSettings.SkippedMovies.Movies.Count > 0)
                    {
                        TraktLogger.Info("Skipping {0} movies due to invalid data or movies don't exist on http://themoviedb.org. Next check will be {1}.", TraktSettings.SkippedMovies.Movies.Count, TraktSettings.SkippedMovies.LastSkippedSync.FromEpoch().Add(new TimeSpan(7,0,0,0)));
                        foreach (var movie in TraktSettings.SkippedMovies.Movies)
                        {
                            TraktLogger.Info("Skipping movie, Title: {0}, Year: {1}, IMDb: {2}", movie.Title, movie.Year, movie.IMDBID);
                            MovieList.RemoveAll(m => (m.Title == movie.Title) && (m.Year.ToString() == movie.Year) && (m.IMDBNumber == movie.IMDBID));
                        }
                    }
                }
                else
                {
                    if (TraktSettings.SkippedMovies.Movies != null) TraktSettings.SkippedMovies.Movies.Clear();
                    TraktSettings.SkippedMovies.LastSkippedSync = DateTime.UtcNow.ToEpoch();
                }
            }
            #endregion

            #region Already Exists Movie Check
            // Remove Already-Exists Movies, these are typically movies that are using aka names and no IMDb/TMDb set
            // When we compare our local collection with trakt collection we have english only titles, so if no imdb/tmdb exists
            // we need to fallback to title matching. When we sync aka names are sometimes accepted if defined on themoviedb.org so we need to 
            // do this to revent syncing these movies every sync interval.
            if (TraktSettings.AlreadyExistMovies != null && TraktSettings.AlreadyExistMovies.Movies != null && TraktSettings.AlreadyExistMovies.Movies.Count > 0)
            {
                TraktLogger.Debug("Skipping {0} movies as they already exist in trakt library but failed local match previously.", TraktSettings.AlreadyExistMovies.Movies.Count.ToString());
                var movies = new List<TraktMovieSync.Movie>(TraktSettings.AlreadyExistMovies.Movies);
                foreach (var movie in movies)
                {
                    Predicate<MFMovie> criteria = m => (m.Title == movie.Title) && (m.Year.ToString() == movie.Year) && (m.IMDBNumber == movie.IMDBID);
                    if (MovieList.Exists(criteria))
                    {
                        TraktLogger.Debug("Skipping movie, Title: {0}, Year: {1}, IMDb: {2}", movie.Title, movie.Year, movie.IMDBID);
                        MovieList.RemoveAll(criteria);
                    }
                    else
                    {
                        // remove as we have now removed from our local collection or updated movie signature
                        if (TraktSettings.MoviePluginCount == 1)
                        {
                            TraktLogger.Debug("Removing 'AlreadyExists' movie, Title: {0}, Year: {1}, IMDb: {2}", movie.Title, movie.Year, movie.IMDBID);
                            TraktSettings.AlreadyExistMovies.Movies.Remove(movie);
                        }
                    }
                }
            }
            #endregion

            TraktLogger.Info("{0} movies available to sync in MyFilms database(s)", MovieList.Count.ToString());

            // get the movies that we have watched
            List<MFMovie> SeenList = MovieList.Where(m => m.Watched == true).ToList();

            TraktLogger.Info("{0} watched movies available to sync in MyFilms database(s)", SeenList.Count.ToString());

            // get the movies that we have yet to watch                        
            IEnumerable<TraktLibraryMovies> traktMoviesAll = TraktAPI.TraktAPI.GetAllMoviesForUser(TraktSettings.Username);
            if (traktMoviesAll == null)
            {
                SyncInProgress = false;
                TraktLogger.Error("Error getting movies from trakt server, cancelling sync.");
                return;
            }
            TraktLogger.Info("{0} movies in trakt library", traktMoviesAll.Count().ToString());

            #region Movies to Sync to Collection
            List<MFMovie> moviesToSync = new List<MFMovie>(MovieList);
            List<TraktLibraryMovies> NoLongerInOurCollection = new List<TraktLibraryMovies>();            
            //Filter out a list of movies we have already sync'd in our collection
            foreach (TraktLibraryMovies tlm in traktMoviesAll)
            {
                bool notInLocalCollection = true;
                // if it is in both libraries
                foreach (MFMovie libraryMovie in MovieList.Where(m => MovieMatch(m, tlm)))
                {
                    // If the users IMDb Id is empty/invalid and we have matched one then set it
                    if (BasicHandler.IsValidImdb(tlm.IMDBID) && !BasicHandler.IsValidImdb(libraryMovie.IMDBNumber))
                    {
                        TraktLogger.Info("Movie '{0}' inserted IMDb Id '{1}'", libraryMovie.Title, tlm.IMDBID);
                        libraryMovie.IMDBNumber = tlm.IMDBID;
                        libraryMovie.Username = TraktSettings.Username;
                        libraryMovie.Commit();
                    }

                    // If the users TMDb Id is empty/invalid and we have one then set it
                    if (string.IsNullOrEmpty(libraryMovie.TMDBNumber) && !string.IsNullOrEmpty(tlm.TMDBID))
                    {
                        TraktLogger.Info("Movie '{0}' inserted TMDb Id '{1}'", libraryMovie.Title, tlm.TMDBID);
                        libraryMovie.TMDBNumber = tlm.TMDBID;
                        libraryMovie.Username = TraktSettings.Username;
                        libraryMovie.Commit();
                    }
                    
                    // if it is watched in Trakt but not My Films update
                    // skip if movie is watched but user wishes to have synced as unseen locally
                    if (tlm.Plays > 0 && !tlm.UnSeen && libraryMovie.Watched == false)
                    {
                        TraktLogger.Info("Movie '{0}' is watched on Trakt updating Database", libraryMovie.Title);
                        libraryMovie.Watched = true;
                        libraryMovie.WatchedCount = tlm.Plays;
                        libraryMovie.Username = TraktSettings.Username; 
                        libraryMovie.Commit();
                    }

                    // mark movies as unseen if watched locally
                    if (tlm.UnSeen && libraryMovie.Watched == true)
                    {
                        TraktLogger.Info("Movie '{0}' is unseen on Trakt, updating database", libraryMovie.Title);
                        libraryMovie.Watched = false;
                        libraryMovie.WatchedCount = tlm.Plays;
                        libraryMovie.Username = TraktSettings.Username; 
                        libraryMovie.Commit();
                    }

                    notInLocalCollection = false;

                    //filter out if its already in collection
                    if (tlm.InCollection)
                    {
                        moviesToSync.RemoveAll(m => MovieMatch(m, tlm));
                    }
                    break;
                }

                if (notInLocalCollection && tlm.InCollection)
                    NoLongerInOurCollection.Add(tlm);
            }
            #endregion

            #region Movies to Sync to Seen Collection
            // filter out a list of movies already marked as watched on trakt
            // also filter out movie marked as unseen so we dont reset the unseen cache online
            List<MFMovie> watchedMoviesToSync = new List<MFMovie>(SeenList);
            foreach (TraktLibraryMovies tlm in traktMoviesAll.Where(t => t.Plays > 0 || t.UnSeen))
            {
                foreach (MFMovie watchedMovie in SeenList.Where(m => MovieMatch(m, tlm)))
                {
                    //filter out
                    watchedMoviesToSync.Remove(watchedMovie);
                }
            }
            #endregion

            #region Send Library/Collection
            TraktLogger.Info("{0} movies need to be added to Library", moviesToSync.Count.ToString());
            foreach (MFMovie m in moviesToSync)
                TraktLogger.Info("Sending movie to trakt library, Title: {0}, Year: {1}, IMDb: {2}, TMDb: {3}", m.Title, m.Year.ToString(), m.IMDBNumber, m.TMDBNumber);

            if (moviesToSync.Count > 0)
            {
                TraktSyncResponse response = TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(moviesToSync), TraktSyncModes.library);
                BasicHandler.InsertSkippedMovies(response);
                BasicHandler.InsertAlreadyExistMovies(response);
                TraktAPI.TraktAPI.LogTraktResponse(response);
            }
            #endregion

            #region Send Seen
            TraktLogger.Info("{0} movies need to be added to SeenList", watchedMoviesToSync.Count.ToString());
            foreach (MFMovie m in watchedMoviesToSync)
              TraktLogger.Info("Sending movie to trakt as seen, Title: {0}, Year: {1}, IMDb: {2}, TMDb: {3}", m.Title, m.Year.ToString(), m.IMDBNumber, m.TMDBNumber);

            if (watchedMoviesToSync.Count > 0)
            {
                TraktSyncResponse response = TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(watchedMoviesToSync), TraktSyncModes.seen);
                BasicHandler.InsertSkippedMovies(response);
                BasicHandler.InsertAlreadyExistMovies(response);
                TraktAPI.TraktAPI.LogTraktResponse(response);
            }
            #endregion

            #region Ratings Sync
            // only sync ratings if we are using Advanced Ratings
            if (TraktSettings.SyncRatings)
            {
                var traktRatedMovies = TraktAPI.TraktAPI.GetUserRatedMovies(TraktSettings.Username);
                if (traktRatedMovies == null)
                    TraktLogger.Error("Error getting rated movies from trakt server.");
                else
                    TraktLogger.Info("{0} rated movies in trakt library", traktRatedMovies.Count().ToString());

                if (traktRatedMovies != null)
                {
                    // get the movies that we have rated/unrated
                    var RatedList = MovieList.Where(m => m.RatingUser > 0.0).ToList();
                    var UnRatedList = MovieList.Except(RatedList).ToList();
                    TraktLogger.Info("{0} rated movies available to sync in MyFilms database", RatedList.Count.ToString());

                    List<MFMovie> ratedMoviesToSync = new List<MFMovie>(RatedList);
                    foreach (var trm in traktRatedMovies)
                    {
                        foreach (var movie in UnRatedList.Where(m => MovieMatch(m, trm)))
                        {
                            // update local collection rating
                            TraktLogger.Info("Inserting rating '{0}/10' for movie '{1} ({2})'", trm.RatingAdvanced, movie.Title, movie.Year);
                            movie.RatingUser = trm.RatingAdvanced;
                            movie.Username = TraktSettings.Username;
                            movie.Commit();
                        }

                        foreach (var movie in RatedList.Where(m => MovieMatch(m, trm)))
                        {
                            // if rating is not synced, update local collection rating to get in sync
                            if ((int)movie.RatingUser != trm.RatingAdvanced)
                            {
                                TraktLogger.Info("Updating rating '{0}/10' for movie '{1} ({2})'", trm.RatingAdvanced, movie.Title, movie.Year);
                                movie.RatingUser = trm.RatingAdvanced;
                                movie.Username = TraktSettings.Username;
                                movie.Commit();
                            }
                            
                            // already rated on trakt, so remove from sync collection
                            ratedMoviesToSync.Remove(movie);
                        }
                    }

                    TraktLogger.Info("{0} rated movies to sync to trakt", ratedMoviesToSync.Count);
                    if (ratedMoviesToSync.Count > 0)
                    {
                        ratedMoviesToSync.ForEach(a => TraktLogger.Info("Importing rating '{0}/10' for movie '{1} ({2})'", a.RatingUser, a.Title, a.Year));
                        TraktResponse response = TraktAPI.TraktAPI.RateMovies(CreateRatingMoviesData(ratedMoviesToSync));
                        TraktAPI.TraktAPI.LogTraktResponse(response);
                    }
                }
            }
            #endregion

            #region Clean Library
            //Dont clean library if more than one movie plugin installed
            if (TraktSettings.KeepTraktLibraryClean && TraktSettings.MoviePluginCount == 1)
            {
                //Remove movies we no longer have in our local database from Trakt
                foreach (var m in NoLongerInOurCollection)
                    TraktLogger.Info("Removing from Trakt Collection {0}", m.Title);

                TraktLogger.Info("{0} movies need to be removed from Trakt Collection", NoLongerInOurCollection.Count.ToString());

                if (NoLongerInOurCollection.Count > 0)
                {
                    if (TraktSettings.AlreadyExistMovies != null && TraktSettings.AlreadyExistMovies.Movies != null && TraktSettings.AlreadyExistMovies.Movies.Count > 0)
                    {
                        TraktLogger.Warning("DISABLING CLEAN LIBRARY!!!, there are trakt library movies that can't be determined to be local in collection.");
                        TraktLogger.Warning("To fix this, check the 'already exist' entries in log, then check movies in local collection against this list and ensure IMDb id is set then run sync again.");
                    }
                    else
                    {
                        //Then remove from library
                        TraktSyncResponse response = TraktAPI.TraktAPI.SyncMovieLibrary(BasicHandler.CreateMovieSyncData(NoLongerInOurCollection), TraktSyncModes.unlibrary);
                        TraktAPI.TraktAPI.LogTraktResponse(response);
                    }
                }
            }
            #endregion

            #region Trakt Category Tags
            List<MFMovie> movieListAll = (from MFMovie movie in myvideos select movie).ToList(); // Add tags also to blocked movies, as it is only local
            // get the movies that locally have trakt categories
            var categoryTraktList = movieListAll.Where(m => m.CategoryTrakt.Count > 0).ToList();
            
            if (TraktSettings.MyFilmsCategories)
            {
                TraktLogger.Info("{0} trakt-categorized movies available in MyFilms database", categoryTraktList.Count.ToString());

                #region update watchlist tags
                IEnumerable<TraktWatchListMovie> traktWatchListMovies = null;
                string Watchlist = Translation.WatchList;
                TraktLogger.Info("Retrieving watchlist from trakt");
                traktWatchListMovies = TraktAPI.TraktAPI.GetWatchListMovies(TraktSettings.Username);

                if (traktWatchListMovies != null)
                {
                    TraktLogger.Info("Retrieved {0} watchlist items from trakt", traktWatchListMovies.Count());

                    var cleanupList = movieListAll.Where(m => m.CategoryTrakt.Contains(Watchlist)).ToList();
                    foreach (var trm in traktWatchListMovies)
                    {
                        TraktLogger.Debug("Processing trakt watchlist movie - Title '{0}', Year '{1}' Imdb '{2}'", trm.Title ?? "null", trm.Year, trm.IMDBID ?? "null");
                        foreach (var movie in movieListAll.Where(m => MovieMatch(m, trm)))
                        {
                            if (!movie.CategoryTrakt.Contains(Watchlist))
                            {
                                TraktLogger.Info("Inserting trakt category '{0}' for movie '{1} ({2})'", Watchlist, movie.Title, movie.Year);
                                movie.CategoryTrakt.Add(Watchlist);
                                movie.Username = TraktSettings.Username;
                                movie.Commit();
                            }
                            cleanupList.Remove(movie);
                        }
                    }
                    // remove tag from remaining films
                    foreach (var movie in cleanupList)
                    {
                        TraktLogger.Info("Removing trakt category '{0}' for movie '{1} ({2})'", Watchlist, movie.Title, movie.Year);
                        movie.CategoryTrakt.Remove(Watchlist);
                        movie.Username = TraktSettings.Username;
                        movie.Commit();
                    }
                }
                #endregion

                #region update user list tags
                IEnumerable<TraktUserList> traktUserLists = null;
                string Userlist = Translation.List;
                TraktLogger.Info("Retrieving user lists from trakt");
                traktUserLists = TraktAPI.TraktAPI.GetUserLists(TraktSettings.Username);

                if (traktUserLists != null)
                {
                    TraktLogger.Info("Retrieved {0} user lists from trakt", traktUserLists.Count());

                    foreach (TraktUserList traktUserList in traktUserLists)
                    {
                        TraktUserList traktUserListMovies = TraktAPI.TraktAPI.GetUserList(TraktSettings.Username, traktUserList.Slug);
                        if (traktUserListMovies == null) continue;

                        string userListName = Userlist + ": " + traktUserList.Name;
                        var cleanupList = movieListAll.Where(m => m.CategoryTrakt.Contains(userListName)).ToList();
                        TraktLogger.Info("Processing trakt user list '{0}' as tag '{1}' with '{2}' items", traktUserList.Name, userListName, traktUserListMovies.Items.Count);

                        // process 'movies' only 
                        foreach (var trm in traktUserListMovies.Items.Where(m => m.Type == "movie"))
                        {
                            TraktLogger.Debug("Processing trakt user list movie - Title '{0}', Year '{1}' ImdbId '{2}'", trm.Title ?? "null", trm.Year ?? "null", trm.ImdbId ?? "null");
                            foreach (var movie in movieListAll.Where(m => MovieMatch(m, trm.Movie)))
                            {
                                if (!movie.CategoryTrakt.Contains(userListName))
                                {
                                    // update local trakt category
                                    TraktLogger.Info("Inserting trakt user list '{0}' for movie '{1} ({2})'", userListName, movie.Title, movie.Year);
                                    movie.CategoryTrakt.Add(userListName);
                                    movie.Username = TraktSettings.Username;
                                    movie.Commit();
                                }
                                cleanupList.Remove(movie);
                            }
                        }

                        // remove tag from remaining films
                        foreach (var movie in cleanupList)
                        {
                            TraktLogger.Info("Removing trakt user list '{0}' for movie '{1} ({2})'", userListName, movie.Title, movie.Year);
                            movie.CategoryTrakt.Remove(userListName);
                            movie.Username = TraktSettings.Username;
                            movie.Commit();
                        }
                    }
                }
                #endregion

                #region update recommendation tags
                IEnumerable<TraktMovie> traktRecommendationMovies = null;
                string Recommendations = Translation.Recommendations;
                TraktLogger.Info("Retrieving recommendations from trakt");
                traktRecommendationMovies = TraktAPI.TraktAPI.GetRecommendedMovies();

                if (traktRecommendationMovies != null)
                {
                    TraktLogger.Info("Retrieved {0} recommendations items from trakt", traktRecommendationMovies.Count());

                    var cleanupList = movieListAll.Where(m => m.CategoryTrakt.Contains(Recommendations)).ToList();
                    foreach (var trm in traktRecommendationMovies)
                    {
                        TraktLogger.Debug("Processing trakt recommendations movie - Title '{0}', Year '{1}' Imdb '{2}'", trm.Title ?? "null", trm.Year ?? "null", trm.IMDBID ?? "null");
                        foreach (var movie in movieListAll.Where(m => MovieMatch(m, trm)))
                        {
                            if (!movie.CategoryTrakt.Contains(Recommendations))
                            {
                                // update local trakt category
                                TraktLogger.Info("Inserting trakt category '{0}' for movie '{1} ({2})'", Recommendations, movie.Title, movie.Year);
                                movie.CategoryTrakt.Add(Recommendations);
                                movie.Username = TraktSettings.Username;
                                movie.Commit();
                            }
                            cleanupList.Remove(movie);
                        }
                    }
                    // remove tag from remaining films
                    foreach (var movie in cleanupList)
                    {
                        // update local trakt category
                        TraktLogger.Info("Removing trakt category '{0}' for movie '{1} ({2})'", Recommendations, movie.Title, movie.Year);
                        movie.CategoryTrakt.Remove(Recommendations);
                        movie.Username = TraktSettings.Username;
                        movie.Commit();
                    }
                }
                #endregion

                #region update trending tags
                /*IEnumerable<TraktTrendingMovie> traktTrendingMovies = null;
                string Trending = Translation.Trending;
                TraktLogger.Info("Retrieving trending movies from trakt");
                traktTrendingMovies = TraktAPI.TraktAPI.GetTrendingMovies();
            
                if (traktTrendingMovies != null)
                {
                    TraktLogger.Info("Retrieved {0} trending items from trakt", traktTrendingMovies.Count());

                    var cleanupList = movieListAll.Where(m => m.CategoryTrakt.Contains(Trending)).ToList();
                    foreach (var trm in traktTrendingMovies)
                    {
                        TraktLogger.Debug("Processing trakt user list movie trm.Title '{0}', trm.Year '{1}' trm.Imdb '{2}'", trm.Title ?? "null", trm.Year ?? "null", trm.Imdb ?? "null");
                        foreach (var movie in movieListAll.Where(m => MovieMatch(m, trm)))
                        {
                            if (!movie.CategoryTrakt.Contains(Trending))
                            {
                                // update local trakt category
                                TraktLogger.Info("Inserting trakt category '{0}' for movie '{1} ({2})'", Trending, movie.Title, movie.Year);
                                movie.CategoryTrakt.Add(Trending);
                                movie.Username = TraktSettings.Username;
                                movie.Commit();
                            }
                            cleanupList.Remove(movie);
                        }
                    }
                    // remove tag from remaining films
                    foreach (var movie in cleanupList)
                    {
                        // update local trakt category
                        TraktLogger.Info("Removing trakt category '{0}' for movie '{1} ({2})'", Trending, movie.Title, movie.Year);
                        movie.CategoryTrakt.Remove(Trending);
                        movie.Username = TraktSettings.Username;
                        movie.Commit();
                    }
                }*/
                #endregion
            }
            else
            {
                if (categoryTraktList.Count > 0)
                {
                    TraktLogger.Info("clearing trakt-categorized movies from MyFilms database", categoryTraktList.Count.ToString());

                    foreach (var movie in categoryTraktList)
                    {
                        movie.CategoryTrakt.Clear();
                        movie.Commit();
                    }
                }
            }
            #endregion

            myvideos.Clear();

            SyncInProgress = false;
            TraktLogger.Info("My Films Sync Completed");
        }

        public bool Scrobble(string filename)
        {
            // check movie is from my films
            if (CurrentMovie == null) return false;

            StopScrobble();

            // create 15 minute timer to send watching status
            #region scrobble timer
            TraktTimer = new Timer(new TimerCallback((stateInfo) =>
            {
                Thread.CurrentThread.Name = "Scrobble";

                MFMovie currentMovie = stateInfo as MFMovie;

                TraktLogger.Info("Scrobbling Movie {0}", currentMovie.Title);
                
                double duration = g_Player.Duration;
                double progress = 0.0;

                // get current progress of player (in seconds) to work out percent complete
                if (duration > 0.0)
                    progress = (g_Player.CurrentPosition / duration) * 100.0;

                // create Scrobbling Data
                TraktMovieScrobble scrobbleData = CreateScrobbleData(currentMovie);
                if (scrobbleData == null) return;

                // set duration/progress in scrobble data
                scrobbleData.Duration = Convert.ToInt32(duration / 60).ToString();
                scrobbleData.Progress = Convert.ToInt32(progress).ToString();

                // set watching status on trakt
                TraktResponse response = TraktAPI.TraktAPI.ScrobbleMovieState(scrobbleData, TraktScrobbleStates.watching);
                TraktAPI.TraktAPI.LogTraktResponse(response);
            }), CurrentMovie, 3000, 900000);
            #endregion

            return true;
        }

        public void StopScrobble()
        {
            if (TraktTimer != null)
                TraktTimer.Dispose();
        }

        #endregion

        #region DataCreators

        /// <summary>
        /// Creates Sync Data based on a List of IMDBMovie objects
        /// </summary>
        /// <param name="Movies">The movies to base the object on</param>
        /// <returns>The Trakt Sync data to send</returns>
        public static TraktMovieSync CreateSyncData(List<MFMovie> Movies)
        {
            string username = TraktSettings.Username;
            string password = TraktSettings.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return null;

            List<TraktMovieSync.Movie> moviesList = (from m in Movies
                                                     select new TraktMovieSync.Movie
                                                     {
                                                         IMDBID = m.IMDBNumber,
                                                         TMDBID = m.TMDBNumber,
                                                         Title = m.Title,
                                                         Year = m.Year.ToString()
                                                     }).ToList();

            TraktMovieSync syncData = new TraktMovieSync
            {
                UserName = username,
                Password = password,
                MovieList = moviesList
            };
            return syncData;
        }

        /// <summary>
        /// Creates Sync Data based on a single IMDBMovie object
        /// </summary>
        /// <param name="Movie">The movie to base the object on</param>
        /// <returns>The Trakt Sync data to send</returns>
        public static TraktMovieSync CreateSyncData(MFMovie Movie)
        {
            string username = TraktSettings.Username;
            string password = TraktSettings.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return null;

            List<TraktMovieSync.Movie> moviesList = new List<TraktMovieSync.Movie>();
            moviesList.Add(new TraktMovieSync.Movie
            {
                IMDBID = Movie.IMDBNumber,
                TMDBID = Movie.TMDBNumber,
                Title = Movie.Title,
                Year = Movie.Year.ToString()
            });

            TraktMovieSync syncData = new TraktMovieSync
            {
                UserName = username,
                Password = password,
                MovieList = moviesList
            };
            return syncData;
        }

        /// <summary>
        /// Creates Scrobble data based on a IMDBMovie object
        /// </summary>
        /// <param name="movie">The movie to base the object on</param>
        /// <returns>The Trakt scrobble data to send</returns>
        public static TraktMovieScrobble CreateScrobbleData(MFMovie movie)
        {
            string username = TraktSettings.Username;
            string password = TraktSettings.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                return null;

            TraktMovieScrobble scrobbleData = new TraktMovieScrobble
            {
                Title = movie.Title,
                Year = movie.Year.ToString(),
                IMDBID = movie.IMDBNumber,
                TMDBID = movie.TMDBNumber,
                PluginVersion = TraktSettings.Version,
                MediaCenter = "Mediaportal",
                MediaCenterVersion = Assembly.GetEntryAssembly().GetName().Version.ToString(),
                MediaCenterBuildDate = String.Empty,
                UserName = username,
                Password = password
            };
            return scrobbleData;
        }

        public static TraktRateMovie CreateRateData(MFMovie movie, String rating)
        {
            string username = TraktSettings.Username;
            string password = TraktSettings.Password;

            if (String.IsNullOrEmpty(username) || String.IsNullOrEmpty(password))
                return null;

            TraktRateMovie rateData = new TraktRateMovie
            {
                Title = movie.Title,
                Year = movie.Year.ToString(),
                IMDBID = movie.IMDBNumber,
                TMDBID = movie.TMDBNumber,
                UserName = username,
                Password = password,
                Rating = rating
            };
            return rateData;
        }

        public static TraktRateMovies CreateRatingMoviesData(List<MFMovie> localMovies)
        {
            if (String.IsNullOrEmpty(TraktSettings.Username) || String.IsNullOrEmpty(TraktSettings.Password))
                return null;

            var traktMovies = from m in localMovies
                              select new TraktRateMovies.Movie
                              {
                                  IMDBID = m.IMDBNumber,
                                  TMDBID = m.TMDBNumber,
                                  Title = m.Title,
                                  Year = m.Year,
                                  Rating = Convert.ToInt32(Math.Round(Convert.ToDecimal(m.RatingUser), MidpointRounding.AwayFromZero))
                              };

            return new TraktRateMovies
            {
                UserName = TraktSettings.Username,
                Password = TraktSettings.Password,
                Movies = traktMovies.ToList()
            };
        }

        #endregion

        #region Public Methods
        
        public void DisposeEvents()
        {
            TraktLogger.Debug("Removing Hooks from My Films");
            
            // unsubscribe from events
            MyFilmsDetail.RateItem -= new MyFilmsDetail.RatingEventDelegate(OnRateItem);
            MyFilmsDetail.WatchedItem -= new MyFilmsDetail.WatchedEventDelegate(OnToggleWatched);
            MyFilmsDetail.MovieStarted -= new MyFilmsDetail.MovieStartedEventDelegate(OnStartedMovie);
            MyFilmsDetail.MovieStopped -= new MyFilmsDetail.MovieStoppedEventDelegate(OnStoppedMovie);
            MyFilmsDetail.MovieWatched -= new MyFilmsDetail.MovieWatchedEventDelegate(OnWatchedMovie);
            MyFilms.ImportComplete -= new MyFilms.ImportCompleteEventDelegate(OnImportComplete);
        }

        #endregion

        #region Player Events

        private void OnStartedMovie(MFMovie movie)
        {
            if (TraktSettings.AccountStatus != ConnectionState.Connected) return;

            if (!TraktSettings.BlockedFilenames.Contains(movie.File) && !TraktSettings.BlockedFolders.Any(f => movie.File.ToLowerInvariant().Contains(f.ToLowerInvariant())))
            {
                TraktLogger.Info("Starting My Films movie playback: '{0}'", movie.Title);
                CurrentMovie = movie;
            }
        }

        private void OnStoppedMovie(MFMovie movie)
        {
            if (TraktSettings.AccountStatus != ConnectionState.Connected) return;

            if (!TraktSettings.BlockedFilenames.Contains(movie.File) && !TraktSettings.BlockedFolders.Any(f => movie.File.ToLowerInvariant().Contains(f.ToLowerInvariant())))
            {
                TraktLogger.Info("Stopped My Films movie playback: '{0}'", movie.Title);

                CurrentMovie = null;
                StopScrobble();

                // send cancelled watching state
                Thread cancelWatching = new Thread(delegate()
                {
                    TraktMovieScrobble scrobbleData = new TraktMovieScrobble { UserName = TraktSettings.Username, Password = TraktSettings.Password };
                    TraktResponse response = TraktAPI.TraktAPI.ScrobbleMovieState(scrobbleData, TraktScrobbleStates.cancelwatching);
                    TraktAPI.TraktAPI.LogTraktResponse(response);
                })
                {
                    IsBackground = true,
                    Name = "CancelWatching"
                };

                cancelWatching.Start();
            }
        }

        private void OnWatchedMovie(MFMovie movie)
        {
            if (TraktSettings.AccountStatus != ConnectionState.Connected) return;
            
            CurrentMovie = null;

            if (!TraktSettings.BlockedFilenames.Contains(movie.File) && !TraktSettings.BlockedFolders.Any(f => movie.File.ToLowerInvariant().Contains(f.ToLowerInvariant())))
            {
                Thread scrobbleMovie = new Thread(delegate(object obj)
                {
                    MFMovie watchedMovie = obj as MFMovie;
                    if (watchedMovie == null) return;

                    // show trakt rating dialog
                    ShowRateDialog(watchedMovie);

                    TraktLogger.Info("My Films movie considered watched: '{0}'", watchedMovie.Title);

                    // get scrobble data to send to api
                    TraktMovieScrobble scrobbleData = CreateScrobbleData(watchedMovie);
                    if (scrobbleData == null) return;

                    // set duration/progress in scrobble data                
                    scrobbleData.Duration = Convert.ToInt32(g_Player.Duration / 60).ToString();
                    scrobbleData.Progress = "100";

                    TraktResponse response = TraktAPI.TraktAPI.ScrobbleMovieState(scrobbleData, TraktScrobbleStates.scrobble);
                    TraktAPI.TraktAPI.LogTraktResponse(response);
                    // UpdateRecommendations();
                })
                {
                    IsBackground = true,
                    Name = "Scrobble"
                };

                scrobbleMovie.Start(movie);
            }
        }

        #endregion

        #region GUI Events

        private void OnRateItem(MFMovie movie, string value)
        {
            TraktLogger.Info("Received rating event from MyFilms");

            if (TraktSettings.AccountStatus != ConnectionState.Connected) return;

            // don't do anything if movie is blocked
            if (TraktSettings.BlockedFilenames.Contains(movie.File) || TraktSettings.BlockedFolders.Any(f => movie.File.ToLowerInvariant().Contains(f.ToLowerInvariant())))
            {
                TraktLogger.Info("Movie {0} is on the blocked list so we didn't update Trakt", movie.Title);
                return;
            }

            // My Films is a 100 point scale out of 10. Treat as decimal and then round off
            string rating = Math.Round(Convert.ToDecimal(value), MidpointRounding.AwayFromZero).ToString();
            TraktRateResponse response = null;

            Thread rateThread = new Thread((o) =>
            {
                MFMovie tMovie = o as MFMovie;

                response = TraktAPI.TraktAPI.RateMovie(CreateRateData(tMovie, rating));

                TraktAPI.TraktAPI.LogTraktResponse(response);
            })
            {
                IsBackground = true,
                Name = "Rate"
            };

            rateThread.Start(movie);
        }

        private void OnToggleWatched(MFMovie movie, bool watched, int count)
        {
            TraktLogger.Info("Received togglewatched event from My Films");

            if (TraktSettings.AccountStatus != ConnectionState.Connected) return;

            // don't do anything if movie is blocked
            if (TraktSettings.BlockedFilenames.Contains(movie.File) || TraktSettings.BlockedFolders.Any(f => movie.File.ToLowerInvariant().Contains(f.ToLowerInvariant())))
            {
                TraktLogger.Info("Movie {0} is on the blocked list so we didn't update Trakt", movie.Title);
                return;
            }

            Thread toggleWatchedThread = new Thread((o) =>
            {
                MFMovie tMovie = o as MFMovie;
                TraktResponse response = TraktAPI.TraktAPI.SyncMovieLibrary(CreateSyncData(tMovie), watched ? TraktSyncModes.seen : TraktSyncModes.unseen);
                TraktAPI.TraktAPI.LogTraktResponse(response);
            })
            {
                IsBackground = true,
                Name = "ToggleWatched"
            };

            toggleWatchedThread.Start(movie);
        }

        #endregion

        #region Import Events

        private void OnImportComplete()
        {
            if (TraktSettings.AccountStatus != ConnectionState.Connected) return;

            TraktLogger.Debug("My Films import complete, initiating online sync");

            // sync again
            Thread syncThread = new Thread(delegate()
            {
                while (SyncInProgress)
                {
                    // only do one sync at a time
                    TraktLogger.Debug("My Films sync still in progress, waiting to complete. Trying again in 60secs.");
                    Thread.Sleep(60000);
                }
                SyncLibrary();
            })
            {
                IsBackground = true,
                Name = "LibrarySync"
            };

            syncThread.Start();
        }       
      
        #endregion

        #region Other Public Methods
        public static bool FindMovie(string title, int year, string imdbid, ref int? movieid, ref string config)
        {
            // get all movies
            ArrayList myvideos = new ArrayList();
            BaseMesFilms.GetMovies(ref myvideos);

            // get all movies in local database
            List<MFMovie> movies = (from MFMovie m in myvideos select m).ToList();

            // try find a match
            MFMovie movie = movies.Find(m => BasicHandler.GetProperMovieImdbId(m.IMDBNumber) == imdbid || (string.Compare(m.Title, title, true) == 0 && m.Year == year));
            if (movie == null) return false;

            movieid = movie.ID;
            config = movie.Config;
            return true;
        }
        #endregion

        #region Private Methods

        private bool MovieMatch(MFMovie mfMovie, TraktMovieBase traktMovie)
        {
            // IMDb comparison
            if (!string.IsNullOrEmpty(traktMovie.IMDBID) && !string.IsNullOrEmpty(BasicHandler.GetProperMovieImdbId(mfMovie.IMDBNumber)))
            {
                return string.Compare(BasicHandler.GetProperMovieImdbId(mfMovie.IMDBNumber), traktMovie.IMDBID, true) == 0;
            }

            // TMDb comparison
            if (!string.IsNullOrEmpty(mfMovie.TMDBNumber) && !string.IsNullOrEmpty(traktMovie.TMDBID))
            {
                return string.Compare(mfMovie.TMDBNumber, traktMovie.TMDBID, true) == 0;
            }

            // Title & Year comparison
            return string.Compare(mfMovie.Title, traktMovie.Title, true) == 0 && mfMovie.Year.ToString() == traktMovie.Year.ToString();
        }

        /// <summary>
        /// Shows the Rate Movie Dialog after playback has ended
        /// </summary>
        /// <param name="movie">The movie being rated</param>
        private void ShowRateDialog(MFMovie movie)
        {
            if (!TraktSettings.ShowRateDialogOnWatched) return;     // not enabled
            // if (movie.RatingUser > 0) return;                       // already rated -> removed, as IF we enable rating, we always want user to rate it....

            TraktLogger.Debug("Showing rate dialog for '{0}'", movie.Title);

            new Thread((o) =>
            {
                MFMovie movieToRate = o as MFMovie;
                if (movieToRate == null) return;

                TraktRateMovie rateObject = new TraktRateMovie
                {
                    IMDBID = movieToRate.IMDBNumber,
                    TMDBID = movieToRate.TMDBNumber,
                    Title = movieToRate.Title,
                    Year = movieToRate.Year.ToString(),
                    UserName = TraktSettings.Username,
                    Password = TraktSettings.Password
                };

                // get the rating submitted to trakt
                int rating = int.Parse(GUI.GUIUtils.ShowRateDialog<TraktRateMovie>(rateObject));

                if (rating > 0)
                {
                    TraktLogger.Debug("Rating {0} as {1}/10", movieToRate.Title, rating.ToString());
                    movieToRate.RatingUser = (float)rating;
                    movieToRate.Username = TraktSettings.Username;
                    movieToRate.Commit();
                }
            })
            {
                Name = "Rate",
                IsBackground = true
            }.Start(movie);
        }

        /// <summary>
        /// Updates the recommended movies to local library
        /// </summary>
        private void UpdateRecommendations()
        {
            ArrayList myvideos = new ArrayList();
            BaseMesFilms.GetMovies(ref myvideos);
            List<MFMovie> movieListAll = (from MFMovie movie in myvideos select movie).ToList(); // Add tags also to blocked movies, as it is only local
            IEnumerable<TraktMovie> traktRecommendationMovies = null;

            traktRecommendationMovies = TraktAPI.TraktAPI.GetRecommendedMovies();
            TraktLogger.Info("Retrieved {0} recommendations items from trakt", traktRecommendationMovies.Count());
            
            if (traktRecommendationMovies != null)
            {
                var cleanupList = movieListAll.Where(m => m.CategoryTrakt.Contains(Translation.Recommendations)).ToList();
                foreach (var trm in traktRecommendationMovies)
                {
                    foreach (var movie in movieListAll.Where(m => MovieMatch(m, trm)))
                    {
                        if (!movie.CategoryTrakt.Contains(Translation.Recommendations))
                        {
                            // update local trakt category
                            TraktLogger.Info("Inserting trakt category '{0}' for movie '{1} ({2})'", Translation.Recommendations, movie.Title, movie.Year);
                            movie.CategoryTrakt.Add(Translation.Recommendations);
                            movie.Username = TraktSettings.Username;
                            movie.Commit();
                        }
                        cleanupList.Remove(movie);
                    }
                }
                // remove tag from remaining films
                foreach (var movie in cleanupList)
                {
                    // update local trakt category
                    TraktLogger.Info("Removing trakt category '{0}' for movie '{1} ({2})'", Translation.Recommendations, movie.Title, movie.Year);
                    movie.CategoryTrakt.Remove(Translation.Recommendations);
                    movie.Username = TraktSettings.Username;
                    movie.Commit();
                }
            }
        }
        #endregion

    }
}
