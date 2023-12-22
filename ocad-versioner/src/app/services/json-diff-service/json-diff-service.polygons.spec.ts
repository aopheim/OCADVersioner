import { JsonDiffService } from './json-diff-service';
import {
  V1_Open_Marsh_Stones,
  V2_EditAllThreePolygons,
  V3_ReplacedMarsh,
  V4_ChangedSymbolOfYellowToGreen,
} from './json-diff-service.spec.models.polygons';

describe('JsonDiffService - Polygons', () => {
  let jsonDiffService: JsonDiffService;
  beforeEach(() => {
    jsonDiffService = new JsonDiffService();
  });

  it('Adding three polygons should give three added', () => {
    const res = jsonDiffService.getJsonDiff(
      { features: [], type: 'FeatureCollection' },
      V1_Open_Marsh_Stones
    );

    expect(res.added.length).toBe(3);
  });

  it('Editing three polygons should give three edited', () => {
    const res = jsonDiffService.getJsonDiff(
      V1_Open_Marsh_Stones,
      V2_EditAllThreePolygons
    );

    expect(res.edited.length).toBe(3);
    expect(res.edited.find((e) => e.symbolNumber === '401')).toBeTruthy();
    expect(res.edited.find((e) => e.symbolNumber === '308')).toBeTruthy();
    expect(res.edited.find((e) => e.symbolNumber === '209')).toBeTruthy();
  });

  it('Replacing marsh should give one deleted, one added', () => {
    const res = jsonDiffService.getJsonDiff(
      V2_EditAllThreePolygons,
      V3_ReplacedMarsh
    );

    expect(res.edited.length).toBe(0);
    expect(res.deleted.length).toBe(1);
    expect(res.added.length).toBe(1);
    expect(res.deleted.find((e) => e.symbolNumber === '308')).toBeTruthy();
    expect(res.added.find((e) => e.symbolNumber === '308')).toBeTruthy();
  });

  it('Change symbol from yellow to green should give one deleted one added', () => {
    const res = jsonDiffService.getJsonDiff(
      V3_ReplacedMarsh,
      V4_ChangedSymbolOfYellowToGreen
    );

    expect(res.edited.length).toBe(0);
    expect(res.deleted.length).toBe(1);
    expect(res.added.length).toBe(1);
    expect(res.deleted.find((e) => e.symbolNumber === '401')).toBeTruthy();
    expect(res.added.find((e) => e.symbolNumber === '406')).toBeTruthy();
  });
});
