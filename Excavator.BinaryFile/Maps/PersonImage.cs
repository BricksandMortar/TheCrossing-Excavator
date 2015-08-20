﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data.Entity;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Excavator.Utility;
using Rock;
using Rock.Data;
using Rock.Model;
using Rock.Storage;
using Rock.Web.Cache;

namespace Excavator.BinaryFile.PersonImage
{
    /// <summary>
    /// Partial of BinaryFile import that holds a Person map
    /// </summary>
    public class PersonImage : BinaryFileComponent, IBinaryFile
    {
        /// <summary>
        /// Maps the specified folder.
        /// </summary>
        /// <param name="folder">The folder.</param>
        /// <param name="fileType">Type of the person image file.</param>
        public void Map( ZipArchive folder, BinaryFileType fileType )
        {
            int completed = 0;

            // check for existing photo?
            //var lookupContext = new RockContext();
            //var existingFileList = new PersonService( lookupContext ).Queryable().AsNoTracking()
            //    .ToDictionary( p => p.Id, f => f.Photo );

            var globalAttributesCache = GlobalAttributesCache.Read();

            var newFileList = new Dictionary<int, Rock.Model.BinaryFile>();

            int totalRows = folder.Entries.Count;
            int percentage = ( totalRows - 1 ) / 100 + 1;
            ReportProgress( 0, string.Format( "Verifying files import ({0:N0} found.", totalRows ) );

            foreach ( var file in folder.Entries )
            {
                var fileExtension = Path.GetExtension( file.Name );
                if ( BinaryFileComponent.FileTypeBlackList.Contains( fileExtension ) )
                {
                    LogException( "Binary File Import", string.Format( "{0} filetype not allowed ({1})", fileExtension, file.Name ) );
                    continue;
                }

                var personForeignId = Path.GetFileNameWithoutExtension( file.Name ).AsType<int?>();
                var personKeys = BinaryFileComponent.ImportedPeople.FirstOrDefault( p => p.IndividualId == personForeignId );
                if ( personKeys != null )
                {
                    var rockFile = new Rock.Model.BinaryFile();
                    rockFile.IsSystem = false;
                    rockFile.IsTemporary = false;
                    rockFile.FileName = file.Name;
                    rockFile.BinaryFileTypeId = fileType.Id;
                    rockFile.CreatedDateTime = file.LastWriteTime.DateTime;
                    rockFile.Description = string.Format( "Imported as {0}", file.Name );
                    rockFile.SetStorageEntityTypeId( fileType.StorageEntityTypeId );
                    rockFile.StorageEntitySettings = fileType.AttributeValues
                        .ToDictionary( a => a.Key, v => v.Value.Value ).ToJson();

                    rockFile.DatabaseData = new BinaryFileData();
                    string content = new StreamReader( file.Open() ).ReadToEnd();

                    byte[] m_Bytes = System.Text.Encoding.UTF8.GetBytes( content );
                    rockFile.DatabaseData.Content = m_Bytes;
                    rockFile.MimeType = Extensions.GetMIMEType( file.Name );

                    // only import the most recent profile photo
                    if ( newFileList.ContainsKey( personKeys.PersonId ) )
                    {
                        var existingPhoto = newFileList[personKeys.PersonId];
                        if ( existingPhoto == null || existingPhoto.CreatedDateTime < rockFile.CreatedDateTime )
                        {
                            newFileList.Add( personKeys.PersonId, rockFile );
                        }
                    }
                    else
                    {
                        newFileList.Add( personKeys.PersonId, rockFile );
                    }

                    completed++;
                    if ( completed % percentage < 1 )
                    {
                        int percentComplete = completed / percentage;
                        ReportProgress( percentComplete, string.Format( "{0:N0} files imported ({1}% complete).", completed, percentComplete ) );
                    }
                    else if ( completed % ReportingNumber < 1 )
                    {
                        SaveFiles( newFileList );

                        // Reset list
                        newFileList.Clear();
                        ReportPartialProgress();
                    }
                }
            }

            if ( newFileList.Any() )
            {
                SaveFiles( newFileList );
            }

            ReportProgress( 100, string.Format( "Finished files import: {0:N0} addresses imported.", completed ) );
        }

        /// <summary>
        /// Saves the files.
        /// </summary>
        /// <param name="newFileList">The new file list.</param>
        private static void SaveFiles( Dictionary<int, Rock.Model.BinaryFile> newFileList )
        {
            var rockContext = new RockContext();
            rockContext.WrapTransaction( () =>
            {
                rockContext.BinaryFiles.AddRange( newFileList.Values );
                rockContext.SaveChanges( DisableAuditing );

                foreach ( var entry in newFileList )
                {
                    // set the path now that we have a guid -- this is normally set by
                    // the MEF storage component which we don't have access to
                    var accessType = entry.Value.MimeType.StartsWith( "image" ) ? "Image" : "File";
                    entry.Value.Path = string.Format( "~/Get{0}.ashx?guid={1}", accessType, entry.Value.Guid );

                    // associate the person with this file
                    rockContext.People.FirstOrDefault( p => p.Id == entry.Key ).PhotoId = entry.Value.Id;
                }

                rockContext.SaveChanges( DisableAuditing );
            } );
        }
    }
}