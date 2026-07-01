import React from 'react';
import Layout from '@theme/Layout';
import SofConformanceMatrix from '@site/src/components/SofConformanceMatrix';

export default function SqlOnFhirConformancePage(): JSX.Element {
  return (
    <Layout
      title="SQL on FHIR Conformance Matrix"
      description="Test-by-test SQL on FHIR v2 conformance results for Ignixa and other known implementations"
    >
      <SofConformanceMatrix />
    </Layout>
  );
}
