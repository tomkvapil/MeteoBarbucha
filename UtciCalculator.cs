using System;

namespace MeteoSrazky
{
    /// <summary>
    /// Standard UTCI (Universal Thermal Climate Index) 6th-order polynomial approximation
    /// according to Bröde et al. (2012), Int J Biometeorol 56:481-494.
    /// </summary>
    public static class UtciCalculator
    {
        public static double Compute(double taCelsius, double rhPercent, double windSpeedKmh, double shortwaveRadWm2)
        {
            double ta = Math.Max(-50.0, Math.Min(50.0, taCelsius));
            double rh = Math.Max(5.0, Math.Min(100.0, rhPercent));
            double va = Math.Max(0.5, Math.Min(17.0, windSpeedKmh / 3.6)); // m/s at 10m
            double sw = Math.Max(0.0, shortwaveRadWm2);

            // Estimate Mean Radiant Temperature delta from global shortwave radiation
            double dTmrt = Math.Max(-30.0, Math.Min(70.0, 0.055 * sw));

            // Saturation vapor pressure (hPa) -> actual vapor pressure (kPa)
            double esHpa = 6.105 * Math.Exp((17.27 * ta) / (237.7 + ta));
            double pa = (rh / 100.0) * esHpa / 10.0; // kPa

            double ta2 = ta * ta;
            double ta3 = ta2 * ta;
            double ta4 = ta3 * ta;
            double ta5 = ta4 * ta;
            double ta6 = ta5 * ta;

            double va2 = va * va;
            double va3 = va2 * va;
            double va4 = va3 * va;
            double va5 = va4 * va;
            double va6 = va5 * va;

            double dt = dTmrt;
            double dt2 = dt * dt;
            double dt3 = dt2 * dt;
            double dt4 = dt3 * dt;
            double dt5 = dt4 * dt;
            double dt6 = dt5 * dt;

            double pa2 = pa * pa;
            double pa3 = pa2 * pa;
            double pa4 = pa3 * pa;
            double pa5 = pa4 * pa;
            double pa6 = pa5 * pa;

            double offset = 6.07562052E-01
                + (-2.27712343E-02) * ta
                + (8.06470249E-04) * ta2
                + (-1.54271372E-04) * ta3
                + (-3.24651735E-06) * ta4
                + (7.32602852E-08) * ta5
                + (1.35959073E-09) * ta6
                + (-2.25836520E+00) * va
                + (8.80326035E-02) * ta * va
                + (2.16844454E-03) * ta2 * va
                + (-1.53347087E-05) * ta3 * va
                + (-5.72983704E-07) * ta4 * va
                + (-2.55090145E-09) * ta5 * va
                + (-7.51269505E-01) * va2
                + (-4.08350271E-03) * ta * va2
                + (-5.21670675E-05) * ta2 * va2
                + (1.94544667E-06) * ta3 * va2
                + (1.14099531E-08) * ta4 * va2
                + (1.58137256E-01) * va3
                + (-6.57263143E-05) * ta * va3
                + (2.22697524E-07) * ta2 * va3
                + (-4.16117031E-08) * ta3 * va3
                + (-1.27762753E-02) * va4
                + (9.66891875E-06) * ta * va4
                + (2.52785852E-09) * ta2 * va4
                + (4.56306672E-04) * va5
                + (-1.74202546E-07) * ta * va5
                + (-5.91491269E-06) * va6
                + (3.98374029E-01) * dt
                + (1.83945314E-04) * ta * dt
                + (-1.73754510E-04) * ta2 * dt
                + (-7.60781159E-07) * ta3 * dt
                + (3.77830287E-08) * ta4 * dt
                + (5.43079673E-10) * ta5 * dt
                + (-2.00518269E-02) * va * dt
                + (8.92859837E-04) * ta * va * dt
                + (3.45433048E-06) * ta2 * va * dt
                + (-3.77925774E-07) * ta3 * va * dt
                + (-1.69699377E-09) * ta4 * va * dt
                + (1.69992415E-04) * va2 * dt
                + (-4.99204314E-05) * ta * va2 * dt
                + (2.47417178E-07) * ta2 * va2 * dt
                + (1.07596466E-08) * ta3 * va2 * dt
                + (8.49242932E-05) * va3 * dt
                + (1.35191328E-06) * ta * va3 * dt
                + (-6.21531254E-09) * ta2 * va3 * dt
                + (-4.99410301E-06) * va4 * dt
                + (-1.89489258E-08) * ta * va4 * dt
                + (8.15300114E-08) * va5 * dt
                + (7.55043090E-04) * dt2
                + (-5.65095215E-05) * ta * dt2
                + (-4.52166564E-07) * ta2 * dt2
                + (2.46688878E-08) * ta3 * dt2
                + (2.42674348E-10) * ta4 * dt2
                + (1.54547250E-04) * va * dt2
                + (5.24110970E-06) * ta * va * dt2
                + (-8.75874982E-08) * ta2 * va * dt2
                + (-1.50743064E-09) * ta3 * va * dt2
                + (-1.56236307E-05) * va2 * dt2
                + (-1.33895614E-07) * ta * va2 * dt2
                + (2.49709824E-09) * ta2 * va2 * dt2
                + (6.51711721E-07) * va3 * dt2
                + (1.94960053E-09) * ta * va3 * dt2
                + (-1.00361113E-08) * va4 * dt2
                + (-1.21206673E-05) * dt3
                + (-2.18203660E-07) * ta * dt3
                + (7.51269482E-09) * ta2 * dt3
                + (9.79063848E-11) * ta3 * dt3
                + (1.25006734E-06) * va * dt3
                + (-1.81584736E-09) * ta * va * dt3
                + (-3.52197671E-10) * ta2 * va * dt3
                + (-3.36514630E-08) * va2 * dt3
                + (1.35908359E-10) * ta * va2 * dt3
                + (4.17032620E-10) * va3 * dt3
                + (-1.30369025E-09) * dt4
                + (4.13908461E-10) * ta * dt4
                + (9.22652254E-12) * ta2 * dt4
                + (-5.08220384E-09) * va * dt4
                + (-2.24730961E-11) * ta * va * dt4
                + (1.17139133E-10) * va2 * dt4
                + (6.62154879E-10) * dt5
                + (4.03863260E-13) * ta * dt5
                + (1.95087203E-12) * va * dt5
                + (-4.73602469E-12) * dt6
                + (5.12733497E+00) * pa
                + (-3.12788561E-01) * ta * pa
                + (-1.96701861E-02) * ta2 * pa
                + (9.99690870E-04) * ta3 * pa
                + (9.51738512E-06) * ta4 * pa
                + (-4.66426341E-07) * ta5 * pa
                + (5.48050612E-01) * va * pa
                + (-3.30552823E-03) * ta * va * pa
                + (-1.64119440E-03) * ta2 * va * pa
                + (-5.16670694E-06) * ta3 * va * pa
                + (9.52692432E-07) * ta4 * va * pa
                + (-4.29223622E-02) * va2 * pa
                + (5.00845667E-03) * ta * va2 * pa
                + (1.00601257E-06) * ta2 * va2 * pa
                + (-1.81748644E-06) * ta3 * va2 * pa
                + (-1.25813502E-03) * va3 * pa
                + (-1.79330391E-04) * ta * va3 * pa
                + (2.34994441E-06) * ta2 * va3 * pa
                + (1.29735808E-04) * va4 * pa
                + (1.29064870E-06) * ta * va4 * pa
                + (-2.28558686E-06) * va5 * pa
                + (-3.69476348E-02) * dt * pa
                + (1.62325322E-03) * ta * dt * pa
                + (-3.14279680E-05) * ta2 * dt * pa
                + (2.59835559E-06) * ta3 * dt * pa
                + (-4.77136523E-08) * ta4 * dt * pa
                + (8.64203390E-03) * va * dt * pa
                + (-6.87405181E-04) * ta * va * dt * pa
                + (-9.13863872E-06) * ta2 * va * dt * pa
                + (5.15916806E-07) * ta3 * va * dt * pa
                + (-3.59217476E-05) * va2 * dt * pa
                + (3.28696511E-05) * ta * va2 * dt * pa
                + (-7.10542454E-07) * ta2 * va2 * dt * pa
                + (-1.24382300E-05) * va3 * dt * pa
                + (-7.38584400E-09) * ta * va3 * dt * pa
                + (2.20609296E-07) * va4 * dt * pa
                + (-7.32469180E-04) * dt2 * pa
                + (-1.87381964E-05) * ta * dt2 * pa
                + (4.80925239E-06) * ta2 * dt2 * pa
                + (-8.75492040E-08) * ta3 * dt2 * pa
                + (2.77862930E-05) * va * dt2 * pa
                + (-5.06004592E-06) * ta * va * dt2 * pa
                + (1.14325367E-07) * ta2 * va * dt2 * pa
                + (2.53016723E-06) * va2 * dt2 * pa
                + (-1.72857035E-08) * ta * va2 * dt2 * pa
                + (-3.95079398E-08) * va3 * dt2 * pa
                + (-3.59413173E-07) * dt3 * pa
                + (7.04388046E-07) * ta * dt3 * pa
                + (-1.89309167E-08) * ta2 * dt3 * pa
                + (-4.79768731E-07) * va * dt3 * pa
                + (7.96079978E-09) * ta * va * dt3 * pa
                + (1.62897058E-09) * va2 * dt3 * pa
                + (3.94367674E-08) * dt4 * pa
                + (-1.18566247E-09) * ta * dt4 * pa
                + (3.34678041E-10) * va * dt4 * pa
                + (-1.15606447E-10) * dt5 * pa
                + (-2.80626406E+00) * pa2
                + (5.48712484E-01) * ta * pa2
                + (-3.99428410E-03) * ta2 * pa2
                + (-9.54009191E-04) * ta3 * pa2
                + (1.93090978E-05) * ta4 * pa2
                + (-3.08806365E-01) * va * pa2
                + (1.16952364E-02) * ta * va * pa2
                + (4.95271903E-04) * ta2 * va * pa2
                + (-1.90710882E-05) * ta3 * va * pa2
                + (2.10787756E-03) * va2 * pa2
                + (-6.98445738E-04) * ta * va2 * pa2
                + (2.30109073E-05) * ta2 * va2 * pa2
                + (4.17856590E-04) * va3 * pa2
                + (-1.27043871E-05) * ta * va3 * pa2
                + (-3.04620472E-06) * va4 * pa2
                + (5.14507424E-02) * dt * pa2
                + (-4.32510997E-03) * ta * dt * pa2
                + (8.99281156E-05) * ta2 * dt * pa2
                + (-7.14663943E-07) * ta3 * dt * pa2
                + (-2.66016305E-04) * va * dt * pa2
                + (2.63789586E-04) * ta * va * dt * pa2
                + (-7.01199003E-06) * ta2 * va * dt * pa2
                + (-1.06823306E-04) * va2 * dt * pa2
                + (3.61341136E-06) * ta * va2 * dt * pa2
                + (2.29748967E-07) * va3 * dt * pa2
                + (3.04788893E-04) * dt2 * pa2
                + (-6.42070836E-05) * ta * dt2 * pa2
                + (1.16257971E-06) * ta2 * dt2 * pa2
                + (7.68023384E-06) * va * dt2 * pa2
                + (-5.47446896E-07) * ta * va * dt2 * pa2
                + (-3.59937910E-08) * va2 * dt2 * pa2
                + (-4.36497725E-06) * dt3 * pa2
                + (1.68737969E-07) * ta * dt3 * pa2
                + (2.67489271E-08) * va * dt3 * pa2
                + (3.23926897E-09) * dt4 * pa2
                + (-3.53874123E-02) * pa3
                + (-2.21201190E-01) * ta * pa3
                + (1.55126038E-02) * ta2 * pa3
                + (-2.63917279E-04) * ta3 * pa3
                + (4.53433455E-02) * va * pa3
                + (-4.32943862E-03) * ta * va * pa3
                + (1.45389826E-04) * ta2 * va * pa3
                + (2.17508610E-04) * va2 * pa3
                + (-6.66724702E-05) * ta * va2 * pa3
                + (3.33217140E-05) * va3 * pa3
                + (-2.26921615E-03) * dt * pa3
                + (3.80261982E-04) * ta * dt * pa3
                + (-5.45314314E-09) * ta2 * dt * pa3
                + (-7.96355448E-04) * va * dt * pa3
                + (2.53458034E-05) * ta * va * dt * pa3
                + (-6.31223658E-06) * va2 * dt * pa3
                + (3.02122035E-04) * dt2 * pa3
                + (-4.77403547E-06) * ta * dt2 * pa3
                + (1.73825715E-06) * va * dt2 * pa3
                + (-4.09087898E-07) * dt3 * pa3
                + (6.14155345E-01) * pa4
                + (-6.16755931E-02) * ta * pa4
                + (1.33374846E-03) * ta2 * pa4
                + (3.55375387E-03) * va * pa4
                + (-5.13027851E-04) * ta * va * pa4
                + (1.02449757E-04) * va2 * pa4
                + (-1.48526421E-03) * dt * pa4
                + (-4.11469183E-05) * ta * dt * pa4
                + (-6.80434415E-06) * va * dt * pa4
                + (-9.77675906E-06) * dt2 * pa4
                + (8.82773108E-02) * pa5
                + (-3.01859306E-03) * ta * pa5
                + (1.04452989E-03) * va * pa5
                + (2.47090539E-04) * dt * pa5
                + (1.48348065E-03) * pa6;

            return ta + offset;
        }
    }
}
