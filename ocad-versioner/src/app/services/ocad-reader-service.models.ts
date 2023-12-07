import { FeatureCollection } from 'geojson';

export interface IOcadReaderService {
  getGeoJsonFromOcdFile(ocadFileHandle: File): Promise<FeatureCollection>;
}
