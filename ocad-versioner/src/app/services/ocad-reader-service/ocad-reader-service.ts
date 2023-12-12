import { readOcad, ocadToGeoJson } from 'ocad2geojson-test';
import { IOcadReaderService } from './ocad-reader-service.models';
import { FeatureCollection } from 'geojson';
import { Injectable } from '@angular/core';
import { Buffer } from 'buffer';

@Injectable()
export class OcadReaderService implements IOcadReaderService {
  async getGeoJsonFromOcdFile(ocadFile: File): Promise<FeatureCollection> {
    const parsedOcdFile = await readOcad(
      Buffer.from(await ocadFile.arrayBuffer())
    );
    return ocadToGeoJson(parsedOcdFile);
  }
}
