﻿using AutoMapper;
using CCL.Importer.Types;
using CCL.Types;
using DV.ThingTypes;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CCL.Importer
{
    internal static class Mapper
    {
        private static readonly MapperConfiguration _config = new(Configure);

        private static IMapper? _map;
        public static IMapper M => _map ??= _config.CreateMapper();

        private static void Configure(IMapperConfigurationExpression cfg)
        {
            cfg.CreateMap<CustomCarVariant, CCL_CarVariant>()
                .ForMember(c => c.parentType, o => o.Ignore());

            cfg.CreateMap<CustomCarType, CCL_CarType>()
                .ForMember(c => c.liveries, o => o.ConvertUsing(new LiveriesConverter()));

            cfg.CreateMap<CustomCarType.BrakesSetup, TrainCarType_v2.BrakesSetup>();
            cfg.CreateMap<CustomCarType.DamageSetup, TrainCarType_v2.DamageSetup>();
        }

        private class LiveriesConverter : IValueConverter<List<CustomCarVariant>, List<TrainCarLivery>>
        {
            public List<TrainCarLivery> Convert(List<CustomCarVariant> sourceMember, ResolutionContext context)
            {
                return sourceMember.Select(v =>
                {
                    var l = ScriptableObject.CreateInstance<CCL_CarVariant>();
                    M.Map(v, l);
                    return l;
                }).Cast<TrainCarLivery>().ToList();
            }
        }
    }
}
