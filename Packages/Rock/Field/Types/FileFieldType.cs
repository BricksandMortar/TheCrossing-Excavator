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
using System.Linq;
using System.IO;
using System.Web.UI;
using System.Web.UI.WebControls;

using Rock;
using Rock.Model;

namespace Rock.Field.Types
{
    /// <summary>
    /// Field used to display or upload a new binary file of a specific type
    /// </summary>
    [Serializable]
    public class FileFieldType : BinaryFileFieldType
    {
        /// <summary>
        /// Creates the control(s) neccessary for prompting user for a new value
        /// </summary>
        /// <param name="configurationValues">The configuration values.</param>
        /// <param name="id"></param>
        /// <returns>
        /// The control
        /// </returns>
        public override Control EditControl( Dictionary<string, ConfigurationValue> configurationValues, string id )
        {
            return new Rock.Web.UI.Controls.FileUploader { ID = id }; 
        }

        /// <summary>
        /// Reads new values entered by the user for the field
        /// </summary>
        /// <param name="control">Parent control that controls were added to in the CreateEditControl() method</param>
        /// <param name="configurationValues"></param>
        /// <returns></returns>
        public override string GetEditValue( Control control, Dictionary<string, ConfigurationValue> configurationValues )
        {
            if ( control != null && control is Rock.Web.UI.Controls.FileUploader )
            {
                var fileSelector = (Rock.Web.UI.Controls.FileUploader)control;

                return fileSelector.BinaryFileId.ToString();
            }
            return null;
        }

        /// <summary>
        /// Sets the value.
        /// </summary>
        /// <param name="control">The control.</param>
        /// <param name="configurationValues"></param>
        /// <param name="value">The value.</param>
        public override void SetEditValue( Control control, Dictionary<string, ConfigurationValue> configurationValues, string value )
        {
            if ( value != null && control != null && control is Rock.Web.UI.Controls.FileUploader )
            {
                var fileSelector = (Rock.Web.UI.Controls.FileUploader)control;

                int binaryFileId = 0;
                if ( Int32.TryParse( value, out binaryFileId ) )
                {
                    fileSelector.BinaryFileId = binaryFileId;
                }
            }
        }
    }
}