import { JsonDiffService } from './json-diff-service';
import {
  V1_Open_Marsh_Stones,
  V2_EditAllThreePolygons,
  V3_ReplacedMarsh,
  V4_ChangedSymbolOfYellowToGreen,
} from './json-diff-service.spec.models.polygons';

///
/// Some tests here fail due to two reasons:
/// - Geometry of Bezier curves is not calculated correctly https://github.com/aopheim/OCADVersioner/issues/23
/// - The conversion from OCAD files to geojson does not preserve the id of the OCAD symbols, regenerating ids on every run. Meaning that the symbols are not compared to itself.
/// https://github.com/perliedman/ocad2geojson/issues/25
/// Do not skip these tests when these issues are fixed.
///

describe('JsonDiffService - Polygons', () => {
  it('Adding three polygons should give three added', () => {
    const res = JsonDiffService.calculateJsonDiff(
      { features: [], type: 'FeatureCollection' },
      V1_Open_Marsh_Stones
    );

    expect(res.added.length).toBe(3);
  });

  it('Editing three polygons should give three edited', () => {
    const res = JsonDiffService.calculateJsonDiff(
      V1_Open_Marsh_Stones,
      V2_EditAllThreePolygons
    );

    expect(res.edited.length).toBe(3);
    expect(res.edited.find((e) => e.symbolNumber === '401')).toBeTruthy();
    expect(res.edited.find((e) => e.symbolNumber === '308')).toBeTruthy();
    expect(res.edited.find((e) => e.symbolNumber === '209')).toBeTruthy();
  });

  xit('Replacing marsh should give one deleted, one added', () => {
    const res = JsonDiffService.calculateJsonDiff(
      V2_EditAllThreePolygons,
      V3_ReplacedMarsh
    );

    expect(res.edited.length).toBe(0);
    expect(res.deleted.length).toBe(1);
    expect(res.added.length).toBe(1);
    expect(res.deleted.find((e) => e.symbolNumber === '308')).toBeTruthy();
    expect(res.added.find((e) => e.symbolNumber === '308')).toBeTruthy();
  });

  xit('Change symbol from yellow to green should give one deleted one added', () => {
    const res = JsonDiffService.calculateJsonDiff(
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
