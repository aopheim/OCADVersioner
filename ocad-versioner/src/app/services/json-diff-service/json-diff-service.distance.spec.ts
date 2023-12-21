import { JsonDiffService } from './json-diff-service';
import {
  V1_GeoreferencedEmptyMap,
  V2_Add100mRoad,
  V3_Add400mPath,
  V4_AddPolygon,
  V5_AddPoint,
  V6_MovePoint100m,
} from './json-diff-service.spec.models.distance';

describe('JsonDiffService - Distance tests', () => {
  let jsonDiffService: JsonDiffService;
  beforeEach(() => {
    jsonDiffService = new JsonDiffService();
  });

  it('Add 100m road should give 100m road added', () => {
    const res = jsonDiffService.getJsonDiff(
      V1_GeoreferencedEmptyMap,
      V2_Add100mRoad
    );

    expect(res.added.length).toBe(1);
    expect(res.added[0].symbolLengthInMeters).toBeGreaterThan(98);
    expect(res.added[0].symbolLengthInMeters).toBeLessThan(102);
  });

  it('Adding 400m circular path should give 400m added', () => {
    const res = jsonDiffService.getJsonDiff(
      V1_GeoreferencedEmptyMap,
      V3_Add400mPath
    );

    expect(res.added.length).toBe(1);
    expect(res.added[0].symbolLengthInMeters).toBeGreaterThan(398);
    expect(res.added[0].symbolLengthInMeters).toBeLessThan(402);
  });

  it('Adding polygon should give added area', () => {
    const res = jsonDiffService.getJsonDiff(
      V1_GeoreferencedEmptyMap,
      V4_AddPolygon
    );

    expect(res.added.length).toBe(1);
    expect(res.added[0].symbolAreaInSquareMeters).toBeGreaterThan(10190);
    expect(res.added[0].symbolLengthInMeters).toBeLessThan(10193);
  });

  it('Moving point 100m should give 100m moved in edited', () => {
    const res = jsonDiffService.getJsonDiff(V5_AddPoint, V6_MovePoint100m);

    expect(res.added.length).toBe(0);
    expect(res.deleted.length).toBe(0);
    expect(res.edited.length).toBe(1);
    expect(res.edited[0].pointSymbolDiff?.movementInMeters).toBeGreaterThan(98);
    expect(res.edited[0].pointSymbolDiff?.movementInMeters).toBeLessThan(102);
  });
});
