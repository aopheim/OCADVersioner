import * as ocad2geojson from 'ocad2geojson';
import { IOcadReaderService } from './ocad-reader-service.models';
import { FeatureCollection } from 'geojson';
import { Injectable } from '@angular/core';
import { Buffer } from 'buffer';

@Injectable()
export class OcadReaderService implements IOcadReaderService {
  async getGeoJsonFromOcdFile(ocadFile: File): Promise<FeatureCollection> {
    const parsedOcdFile = await ocad2geojson.readOcad(
      Buffer.from(await ocadFile.arrayBuffer())
    );
    return ocad2geojson.ocadToGeoJson(parsedOcdFile) as FeatureCollection;
  }
}
