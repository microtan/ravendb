﻿using Raven.Client.Indexes;
using Raven.Tests.Core.Utils.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raven.Tests.Core.Utils.Indexes
{
    public class CameraCost : AbstractIndexCreationTask<Camera>
    {
        public CameraCost()
        {
            Map = cameras => from camera in cameras
                             select new
                             {
                                 Id = camera.Id,
                                 Manufacturer = camera.Manufacturer,
                                 Model = camera.Model,
                                 Cost = camera.Cost,
                                 Zoom = camera.Zoom,
                                 Megapixels = camera.Megapixels
                             };
        }
    }
}
