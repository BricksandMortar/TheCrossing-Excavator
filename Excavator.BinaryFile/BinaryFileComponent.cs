﻿// <copyright>
// Copyright 2013 by the Spark Development Network
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Configuration;
using System.Data;
using System.Data.Entity;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Excavator;
using Excavator.BinaryFile;
using Excavator.Utility;
using Rock;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;

namespace Excavator.BinaryFile
{
    /// <summary>
    /// Data models and mapping methods to import binary files
    /// </summary>
    [Export( typeof( ExcavatorComponent ) )]
    public class BinaryFileComponent : ExcavatorComponent
    {
        #region Fields

        /// <summary>
        /// Gets the full name of the excavator type.
        /// </summary>
        /// <value>
        /// The name of the database being imported.
        /// </value>
        public override string FullName
        {
            get { return "Binary File"; }
        }

        /// <summary>
        /// Gets the supported file extension type(s).
        /// </summary>
        /// <value>
        /// The supported extension type(s).
        /// </value>
        public override string ExtensionType
        {
            get { return ".zip"; }
        }

        /// All the people who've been imported
        protected static List<PersonKeys> ImportedPeople;

        // Database StorageEntity Type
        protected static int? DatabaseStorageTypeId;

        // File System StorageEntity Type
        protected static int? FileSystemStorageTypeId;

        // Binary File RootPath Attribute
        protected static AttributeCache RootPathAttribute;

        // Maintains compatibility with core blacklist
        protected static IEnumerable<string> FileTypeBlackList;

        /// <summary>
        /// The file types
        /// </summary>
        protected static List<BinaryFileType> FileTypes;

        /// <summary>
        /// The person assigned to do the import
        /// </summary>
        protected static int? ImportPersonAliasId;

        #endregion Fields

        #region Methods

        /// <summary>
        /// Loads the database for this instance.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public override bool LoadSchema( string fileName )
        {
            if ( DataNodes == null )
            {
                DataNodes = new List<DataNode>();
            }

            var folderItem = new DataNode();
            var previewFolder = new ZipArchive( new FileStream( fileName, FileMode.Open ) );
            folderItem.Name = Path.GetFileNameWithoutExtension( fileName );
            folderItem.Path = fileName;

            foreach ( var document in previewFolder.Entries.Take( 50 ) )
            {
                if ( document != null )
                {
                    var entryItem = new DataNode();
                    entryItem.Name = document.FullName;
                    string content = new StreamReader( document.Open() ).ReadToEnd();
                    entryItem.Value = Encoding.UTF8.GetBytes( content ) ?? null;
                    entryItem.NodeType = typeof( byte[] );
                    entryItem.Parent.Add( folderItem );
                    folderItem.Children.Add( entryItem );
                }
            }

            DataNodes.Add( folderItem );

            return DataNodes.Count() > 0 ? true : false;
        }

        /// <summary>
        /// Transforms the data from the dataset.
        /// </summary>
        public override int TransformData( Dictionary<string, string> settings )
        {
            var importUser = settings["ImportUser"];

            ReportProgress( 0, "Starting health checks..." );
            var rockContext = new RockContext();
            var personService = new PersonService( rockContext );
            var importPerson = personService.GetByFullName( importUser, allowFirstNameOnly: true ).FirstOrDefault();

            if ( importPerson == null )
            {
                importPerson = personService.Queryable().AsNoTracking().FirstOrDefault();
            }

            ImportPersonAliasId = importPerson.PrimaryAliasId;
            ReportProgress( 0, "Checking for existing attributes..." );
            LoadRockData( rockContext );

            // only import things that the user checked
            var selectedFiles = DataNodes.Where( n => n.Checked != false ).ToList();

            foreach ( var file in selectedFiles )
            {
                var archiveFolder = new ZipArchive( new FileStream( file.Path, FileMode.Open ) );

                IBinaryFile worker = IMapAdapterFactory.GetAdapter( file.Name );
                if ( worker != null )
                {
                    worker.Map( archiveFolder, FileTypes.FirstOrDefault( t => file.Name.RemoveWhitespace().StartsWith( t.Name.RemoveWhitespace() ) ) );
                }
                else
                {
                    LogException( "Binary File", string.Format( "Unknown File: {0} does not start with the name of a known data map.", file.Name ) );
                }
            }

            // Report the final imported count
            ReportProgress( 100, string.Format( "Completed import: {0:N0} records imported.", 100 ) );
            return 0;
        }

        /// <summary>
        /// Loads Rock data that's used globally by the transform
        /// </summary>
        private void LoadRockData( RockContext lookupContext = null )
        {
            lookupContext = lookupContext ?? new RockContext();

            // core-specified attribute guid for setting file root path
            RootPathAttribute = AttributeCache.Read( new Guid( "3CAFA34D-9208-439B-A046-CB727FB729DE" ) );

            // core-specified blacklist files
            FileTypeBlackList = ( GlobalAttributesCache.Read().GetValue( "ContentFiletypeBlacklist" )
                ?? string.Empty ).Split( new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries );

            // clean up blacklist
            FileTypeBlackList = FileTypeBlackList.Select( a => a.ToLower().TrimStart( new char[] { '.', ' ' } ) );

            DatabaseStorageTypeId = EntityTypeCache.GetId( typeof( Rock.Storage.Provider.Database ) );
            FileSystemStorageTypeId = EntityTypeCache.GetId( typeof( Rock.Storage.Provider.FileSystem ) );
            FileTypes = new BinaryFileTypeService( lookupContext ).Queryable().AsNoTracking().ToList();

            // get all the types we'll be importing
            var binaryTypeSettings = ConfigurationManager.GetSection( "binaryFileTypes" ) as NameValueCollection;

            // create any custom types defined in settings that don't exist yet
            foreach ( var typeKey in binaryTypeSettings.AllKeys )
            {
                var newFileType = FileTypes.FirstOrDefault( f => f.Name == typeKey );
                if ( newFileType == null )
                {
                    newFileType = new BinaryFileType();
                    newFileType.Name = typeKey;
                    newFileType.Description = typeKey;
                    newFileType.AllowCaching = true;

                    var typeValue = binaryTypeSettings[typeKey];
                    if ( typeValue != null )
                    {
                        newFileType.StorageEntityTypeId = typeValue.Equals( "Database" ) ? DatabaseStorageTypeId : FileSystemStorageTypeId;
                        newFileType.Attributes = new Dictionary<string, AttributeCache>();
                        newFileType.AttributeValues = new Dictionary<string, AttributeValue>();

                        newFileType.Attributes.Add( RootPathAttribute.Key, RootPathAttribute );
                        newFileType.AttributeValues.Add( RootPathAttribute.Key, new AttributeValue()
                        {
                            AttributeId = RootPathAttribute.Id,
                            Value = typeValue
                        } );
                    }

                    lookupContext.BinaryFileTypes.Add( newFileType );
                    lookupContext.SaveChanges();

                    FileTypes.Add( newFileType );
                }
            }

            // load attributes to get the default storage location
            foreach ( var type in FileTypes )
            {
                type.LoadAttributes( lookupContext );
            }

            // get a list of all the imported people keys
            var personAliasList = new PersonAliasService( lookupContext ).Queryable().AsNoTracking().ToList();
            ImportedPeople = personAliasList.Select( pa =>
                new PersonKeys()
                {
                    PersonAliasId = pa.Id,
                    PersonId = pa.PersonId,
                    IndividualId = pa.ForeignId.AsType<int?>(),
                } ).ToList();
        }

        #endregion Methods
    }

    #region Helper Classes

    /// <summary>
    /// Generic map interface
    /// </summary>
    public interface IBinaryFile
    {
        void Map( ZipArchive zipData, BinaryFileType fileType );
    }

    /// <summary>
    /// Adapter helper method to call the right object Map()
    /// </summary>
    public static class IMapAdapterFactory
    {
        public static IBinaryFile GetAdapter( string fileName )
        {
            IBinaryFile adapter = null;

            var configFileTypes = ConfigurationManager.GetSection( "binaryFileTypes" ) as NameValueCollection;

            // ensure the file matches a config type?
            //if ( configFileTypes != null && configFileTypes.AllKeys.Any( k => fileName.StartsWith( k.RemoveWhitespace() ) ) )
            //{
            var iBinaryFileType = typeof( IBinaryFile );
            var mappedFileTypes = iBinaryFileType.Assembly.ExportedTypes
                .Where( p => iBinaryFileType.IsAssignableFrom( p ) && !p.IsInterface );
            var selectedType = mappedFileTypes.FirstOrDefault( t => fileName.StartsWith( t.Name.RemoveWhitespace() ) );
            if ( selectedType != null )
            {
                adapter = (IBinaryFile)Activator.CreateInstance( selectedType );
            }
            else
            {
                adapter = new MinistryDocument();
            }

            //}

            return adapter;
        }
    }

    #endregion
}