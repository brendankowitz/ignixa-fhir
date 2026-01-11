import React from 'react';
import Layout from '@theme/Layout';
import BenchmarkDashboard from '@site/src/components/BenchmarkDashboard';

export default function BenchmarksDashboardPage(): JSX.Element {
  return (
    <Layout
      title="Performance Dashboard"
      description="Interactive FHIRPath performance benchmarks comparing Ignixa vs Firely SDK"
    >
      <BenchmarkDashboard />
    </Layout>
  );
}
