import {
  readOcad,
  ocadToGeoJson,
  OcadFile,
  ocadToSvg,
} from 'ocad2geojson-test';
import { IOcadReaderService } from './ocad-reader-service.models';
import { FeatureCollection, GeoJsonProperties, Geometry } from 'geojson';
import { Injectable } from '@angular/core';
import { Buffer } from 'buffer';

@Injectable()
export class OcadReaderService implements IOcadReaderService {
  async getOcdObjectFromOcdFile(ocadFileHandle: File): Promise<OcadFile> {
    return await readOcad(Buffer.from(await ocadFileHandle.arrayBuffer()));
  }

  async getGeoJsonFromOcadObject(
    ocadObject: OcadFile
  ): Promise<FeatureCollection<Geometry, GeoJsonProperties>> {
    return await ocadToGeoJson(ocadObject, { generateSymbolElements: false });
  }

  async getGeoJsonFromOcdFile(ocadFile: File): Promise<FeatureCollection> {
    const parsedOcdFile = await this.getOcdObjectFromOcdFile(ocadFile);
    return await this.getGeoJsonFromOcadObject(parsedOcdFile);
  }

  async getSvgFromOcadObject(ocadFileHandle: File): Promise<SVGElement> {
    const parsedOcdFile = await this.getOcdObjectFromOcdFile(ocadFileHandle);
    return await ocadToSvg(parsedOcdFile);
  }
}
